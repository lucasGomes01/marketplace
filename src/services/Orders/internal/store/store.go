// Package store implementa app.Repository no SQL Server (go-mssqldb). Banco e tabelas são criados se não
// existirem (o equivalente às migrations aplicadas no startup dos serviços .NET).
package store

import (
	"context"
	"database/sql"
	"errors"
	"fmt"
	"net/url"
	"regexp"
	"strings"
	"time"

	mssql "github.com/microsoft/go-mssqldb"

	"marketplace/orders/internal/app"
	"marketplace/orders/internal/order"
)

var databaseName = regexp.MustCompile(`^[A-Za-z][A-Za-z0-9_]*$`)

const schema = `
IF OBJECT_ID('dbo.Orders') IS NULL
CREATE TABLE dbo.Orders (
    Id              NVARCHAR(36)   NOT NULL PRIMARY KEY,
    BuyerId         NVARCHAR(36)   NOT NULL,
    CartId          NVARCHAR(36)   NOT NULL,
    Status          NVARCHAR(20)   NOT NULL,
    RejectionReason NVARCHAR(500)  NULL,
    CreatedAt       DATETIMEOFFSET NOT NULL,
    UpdatedAt       DATETIMEOFFSET NOT NULL,
    INDEX IX_Orders_BuyerId_CreatedAt (BuyerId, CreatedAt DESC)
);

IF OBJECT_ID('dbo.OrderItems') IS NULL
CREATE TABLE dbo.OrderItems (
    OrderId        NVARCHAR(36)  NOT NULL REFERENCES dbo.Orders(Id) ON DELETE CASCADE,
    Position       INT           NOT NULL,
    ProductId      NVARCHAR(36)  NOT NULL,
    SellerId       NVARCHAR(36)  NOT NULL,
    ProductName    NVARCHAR(120) NOT NULL,
    UnitPriceCents BIGINT        NOT NULL,
    Currency       NCHAR(3)      NOT NULL,
    Quantity       INT           NOT NULL,
    PRIMARY KEY (OrderId, Position)
);

IF OBJECT_ID('dbo.OrderShipments') IS NULL
CREATE TABLE dbo.OrderShipments (
    OrderId     NVARCHAR(36)   NOT NULL REFERENCES dbo.Orders(Id) ON DELETE CASCADE,
    SellerId    NVARCHAR(36)   NOT NULL,
    Position    INT            NOT NULL,
    Status      NVARCHAR(20)   NOT NULL,
    PickedAt    DATETIMEOFFSET NULL,
    PackedAt    DATETIMEOFFSET NULL,
    ShippedAt   DATETIMEOFFSET NULL,
    DeliveredAt DATETIMEOFFSET NULL,
    PRIMARY KEY (OrderId, SellerId),
    INDEX IX_OrderShipments_SellerId (SellerId)
);

-- Backfill: pedidos gravados antes dos envios (T-024) ganham um envio por vendedor. Idempotente.
INSERT INTO dbo.OrderShipments (OrderId, SellerId, Position, Status)
SELECT i.OrderId, i.SellerId, MIN(i.Position), 'AwaitingPicking'
FROM dbo.OrderItems i
WHERE NOT EXISTS (SELECT 1 FROM dbo.OrderShipments s WHERE s.OrderId = i.OrderId AND s.SellerId = i.SellerId)
GROUP BY i.OrderId, i.SellerId;`

// Store guarda pedidos no SQL Server.
type Store struct{ db *sql.DB }

// Open garante que o banco (parâmetro "database" do DSN) e as tabelas existem e devolve o Store.
func Open(ctx context.Context, dsn string) (*Store, error) {
	if err := ensureDatabase(ctx, dsn); err != nil {
		return nil, err
	}
	db, err := sql.Open("sqlserver", dsn)
	if err != nil {
		return nil, err
	}
	if _, err := db.ExecContext(ctx, schema); err != nil {
		_ = db.Close()
		return nil, fmt.Errorf("create schema: %w", err)
	}
	return &Store{db: db}, nil
}

func ensureDatabase(ctx context.Context, dsn string) error {
	u, err := url.Parse(dsn)
	if err != nil {
		return fmt.Errorf("parse dsn: %w", err)
	}
	query := u.Query()
	name := query.Get("database")
	if !databaseName.MatchString(name) {
		return fmt.Errorf("dsn needs a valid database parameter, got %q", name)
	}
	query.Set("database", "master")
	u.RawQuery = query.Encode()

	master, err := sql.Open("sqlserver", u.String())
	if err != nil {
		return err
	}
	defer master.Close()
	// O nome foi validado pelo regex acima; identificadores não aceitam parâmetro.
	_, err = master.ExecContext(ctx, fmt.Sprintf("IF DB_ID('%s') IS NULL CREATE DATABASE [%s]", name, name))
	if err != nil {
		return fmt.Errorf("create database %s: %w", name, err)
	}
	return nil
}

// Close fecha as conexões.
func (s *Store) Close() error { return s.db.Close() }

// Insert grava pedido e itens numa transação. Id repetido → app.ErrDuplicate.
func (s *Store) Insert(ctx context.Context, o *order.Order) (err error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer func() {
		if err != nil {
			_ = tx.Rollback()
		}
	}()

	_, err = tx.ExecContext(ctx,
		`INSERT INTO dbo.Orders (Id, BuyerId, CartId, Status, RejectionReason, CreatedAt, UpdatedAt)
		 VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7)`,
		o.ID, o.BuyerID, o.CartID, string(o.Status), nullable(o.RejectionReason),
		mssql.DateTimeOffset(o.CreatedAt), mssql.DateTimeOffset(o.UpdatedAt))
	if isUniqueViolation(err) {
		return app.ErrDuplicate
	}
	if err != nil {
		return fmt.Errorf("insert order: %w", err)
	}

	for i, item := range o.Items {
		_, err = tx.ExecContext(ctx,
			`INSERT INTO dbo.OrderItems (OrderId, Position, ProductId, SellerId, ProductName, UnitPriceCents, Currency, Quantity)
			 VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8)`,
			o.ID, i+1, item.ProductID, item.SellerID, item.ProductName, item.UnitPrice.Cents, item.UnitPrice.Currency, item.Quantity)
		if err != nil {
			return fmt.Errorf("insert item %d: %w", i+1, err)
		}
	}
	for i, shipment := range o.Shipments {
		_, err = tx.ExecContext(ctx,
			`INSERT INTO dbo.OrderShipments (OrderId, SellerId, Position, Status, PickedAt, PackedAt, ShippedAt, DeliveredAt)
			 VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8)`,
			o.ID, shipment.SellerID, i+1, string(shipment.Status), nullableTime(shipment.PickedAt),
			nullableTime(shipment.PackedAt), nullableTime(shipment.ShippedAt), nullableTime(shipment.DeliveredAt))
		if err != nil {
			return fmt.Errorf("insert shipment %s: %w", shipment.SellerID, err)
		}
	}
	return tx.Commit()
}

// UpdateShipment grava o envio do vendedor e o UpdatedAt do pedido, só se o envio ainda estiver em previous.
func (s *Store) UpdateShipment(ctx context.Context, o *order.Order, sellerID string, previous order.ShipmentStatus) (err error) {
	shipment, ok := o.Shipment(sellerID)
	if !ok {
		return app.ErrNotFound
	}
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer func() {
		if err != nil {
			_ = tx.Rollback()
		}
	}()

	result, err := tx.ExecContext(ctx,
		`UPDATE dbo.OrderShipments SET Status = @p4, PickedAt = @p5, PackedAt = @p6, ShippedAt = @p7, DeliveredAt = @p8
		 WHERE OrderId = @p1 AND SellerId = @p2 AND Status = @p3`,
		o.ID, sellerID, string(previous), string(shipment.Status), nullableTime(shipment.PickedAt),
		nullableTime(shipment.PackedAt), nullableTime(shipment.ShippedAt), nullableTime(shipment.DeliveredAt))
	if err != nil {
		return fmt.Errorf("update shipment: %w", err)
	}
	if n, _ := result.RowsAffected(); n == 0 {
		return app.ErrConflict
	}
	if _, err = tx.ExecContext(ctx, `UPDATE dbo.Orders SET UpdatedAt = @p2 WHERE Id = @p1`,
		o.ID, mssql.DateTimeOffset(o.UpdatedAt)); err != nil {
		return fmt.Errorf("update order: %w", err)
	}
	return tx.Commit()
}

// UpdateStatus grava a mudança de status (os itens nunca mudam depois de criados).
func (s *Store) UpdateStatus(ctx context.Context, o *order.Order) error {
	result, err := s.db.ExecContext(ctx,
		`UPDATE dbo.Orders SET Status = @p2, RejectionReason = @p3, UpdatedAt = @p4 WHERE Id = @p1`,
		o.ID, string(o.Status), nullable(o.RejectionReason), mssql.DateTimeOffset(o.UpdatedAt))
	if err != nil {
		return fmt.Errorf("update order: %w", err)
	}
	if n, _ := result.RowsAffected(); n == 0 {
		return app.ErrNotFound
	}
	return nil
}

// Get carrega o pedido com os itens. Inexistente → app.ErrNotFound.
func (s *Store) Get(ctx context.Context, id string) (*order.Order, error) {
	rows, err := s.db.QueryContext(ctx, orderColumns+` WHERE Id = @p1`, id)
	if err != nil {
		return nil, err
	}
	orders, err := s.scanOrders(ctx, rows)
	if err != nil {
		return nil, err
	}
	if len(orders) == 0 {
		return nil, app.ErrNotFound
	}
	return orders[0], nil
}

// ListByBuyer devolve até limit pedidos do comprador, mais recentes primeiro.
func (s *Store) ListByBuyer(ctx context.Context, buyerID string, limit int) ([]*order.Order, error) {
	rows, err := s.db.QueryContext(ctx, `SELECT TOP (@p2) `+strings.TrimPrefix(orderColumns, "SELECT ")+
		` WHERE BuyerId = @p1 ORDER BY CreatedAt DESC, Id DESC`, buyerID, limit)
	if err != nil {
		return nil, err
	}
	return s.scanOrders(ctx, rows)
}

// ListBySeller devolve até limit pedidos Confirmed com envio do vendedor, mais recentes primeiro.
func (s *Store) ListBySeller(ctx context.Context, sellerID string, limit int) ([]*order.Order, error) {
	rows, err := s.db.QueryContext(ctx, `SELECT TOP (@p2) `+strings.TrimPrefix(orderColumns, "SELECT ")+
		` WHERE Status = @p3 AND Id IN (SELECT OrderId FROM dbo.OrderShipments WHERE SellerId = @p1)
		 ORDER BY CreatedAt DESC, Id DESC`, sellerID, limit, string(order.StatusConfirmed))
	if err != nil {
		return nil, err
	}
	return s.scanOrders(ctx, rows)
}

const orderColumns = `SELECT Id, BuyerId, CartId, Status, RejectionReason, CreatedAt, UpdatedAt FROM dbo.Orders`

func (s *Store) scanOrders(ctx context.Context, rows *sql.Rows) ([]*order.Order, error) {
	var orders []*order.Order
	byID := map[string]*order.Order{}
	for rows.Next() {
		var o order.Order
		var status string
		var reason sql.NullString
		if err := rows.Scan(&o.ID, &o.BuyerID, &o.CartID, &status, &reason, &o.CreatedAt, &o.UpdatedAt); err != nil {
			_ = rows.Close()
			return nil, err
		}
		o.Status, o.RejectionReason = order.Status(status), reason.String
		o.CreatedAt, o.UpdatedAt = o.CreatedAt.UTC(), o.UpdatedAt.UTC()
		orders = append(orders, &o)
		byID[o.ID] = &o
	}
	if err := rows.Close(); err != nil {
		return nil, err
	}
	if len(orders) == 0 {
		return nil, nil
	}
	if err := s.loadItems(ctx, byID); err != nil {
		return nil, err
	}
	return orders, s.loadShipments(ctx, byID)
}

// inClause monta "IN (@p1, @p2...)" com os ids dos pedidos.
func inClause(byID map[string]*order.Order) (string, []any) {
	ids := make([]any, 0, len(byID))
	placeholders := make([]string, 0, len(byID))
	for id := range byID {
		ids = append(ids, id)
		placeholders = append(placeholders, fmt.Sprintf("@p%d", len(ids)))
	}
	return "IN (" + strings.Join(placeholders, ", ") + ")", ids
}

func (s *Store) loadShipments(ctx context.Context, byID map[string]*order.Order) error {
	in, ids := inClause(byID)
	rows, err := s.db.QueryContext(ctx,
		`SELECT OrderId, SellerId, Status, PickedAt, PackedAt, ShippedAt, DeliveredAt FROM dbo.OrderShipments
		 WHERE OrderId `+in+` ORDER BY OrderId, Position`, ids...)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var orderID, status string
		var shipment order.Shipment
		var picked, packed, shipped, delivered sql.NullTime
		if err := rows.Scan(&orderID, &shipment.SellerID, &status, &picked, &packed, &shipped, &delivered); err != nil {
			return err
		}
		shipment.Status = order.ShipmentStatus(status)
		shipment.PickedAt, shipment.PackedAt = timeOf(picked), timeOf(packed)
		shipment.ShippedAt, shipment.DeliveredAt = timeOf(shipped), timeOf(delivered)
		byID[orderID].Shipments = append(byID[orderID].Shipments, shipment)
	}
	return rows.Err()
}

func (s *Store) loadItems(ctx context.Context, byID map[string]*order.Order) error {
	in, ids := inClause(byID)
	rows, err := s.db.QueryContext(ctx,
		`SELECT OrderId, ProductId, SellerId, ProductName, UnitPriceCents, Currency, Quantity FROM dbo.OrderItems
		 WHERE OrderId `+in+` ORDER BY OrderId, Position`, ids...)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var orderID string
		var item order.Item
		if err := rows.Scan(&orderID, &item.ProductID, &item.SellerID, &item.ProductName,
			&item.UnitPrice.Cents, &item.UnitPrice.Currency, &item.Quantity); err != nil {
			return err
		}
		byID[orderID].Items = append(byID[orderID].Items, item)
	}
	return rows.Err()
}

func nullable(s string) any {
	if s == "" {
		return nil
	}
	return s
}

func nullableTime(t time.Time) any {
	if t.IsZero() {
		return nil
	}
	return mssql.DateTimeOffset(t)
}

func timeOf(t sql.NullTime) time.Time {
	if !t.Valid {
		return time.Time{}
	}
	return t.Time.UTC()
}

func isUniqueViolation(err error) bool {
	var sqlErr mssql.Error
	return errors.As(err, &sqlErr) && (sqlErr.Number == 2627 || sqlErr.Number == 2601)
}

// pingTimeout limita a espera inicial pelo banco no startup.
const pingTimeout = 60 * time.Second

// WaitReady tenta conectar até o banco responder (o SQL Server demora a subir no compose).
func WaitReady(ctx context.Context, dsn string) error {
	ctx, cancel := context.WithTimeout(ctx, pingTimeout)
	defer cancel()
	for {
		u, _ := url.Parse(dsn)
		q := u.Query()
		q.Set("database", "master")
		u.RawQuery = q.Encode()
		db, err := sql.Open("sqlserver", u.String())
		if err == nil {
			err = db.PingContext(ctx)
			_ = db.Close()
		}
		if err == nil {
			return nil
		}
		select {
		case <-ctx.Done():
			return fmt.Errorf("database not ready: %w", err)
		case <-time.After(2 * time.Second):
		}
	}
}
