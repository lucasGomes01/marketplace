// Package app tem os casos de uso do Orders. Ele define as interfaces que usa (Repository, Publisher);
// store e messaging as implementam — a dependência aponta para cá (mesma ideia do ADR-004 no .NET).
package app

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"time"

	"marketplace/orders/internal/order"
)

var (
	// ErrDuplicate: já existe pedido com esse id (checkout repetido).
	ErrDuplicate = errors.New("app: order already exists")
	// ErrNotFound: pedido inexistente ou de outro comprador.
	ErrNotFound = errors.New("app: order not found")
	// ErrInvalidEvent: evento que nunca vai dar certo (dado inválido); o consumidor registra e pula.
	ErrInvalidEvent = errors.New("app: invalid event")
)

// Repository é a porta de persistência.
type Repository interface {
	Insert(ctx context.Context, o *order.Order) error
	// UpdateStatus grava status, motivo e UpdatedAt. Inexistente → ErrNotFound.
	UpdateStatus(ctx context.Context, o *order.Order) error
	Get(ctx context.Context, id string) (*order.Order, error)
	ListByBuyer(ctx context.Context, buyerID string, limit int) ([]*order.Order, error)
}

// Publisher é a porta de publicação de eventos (envelope igual ao dos serviços .NET).
type Publisher interface {
	Publish(ctx context.Context, eventType, key, eventID string, data any) error
}

// Tipos de evento (= nomes dos tópicos, ver .ai/context/events.md).
const (
	EventCartCheckedOut = "cart.checked-out"
	EventOrderPlaced    = "order.placed"
	EventStockReserved  = "stock.reserved"
	EventStockRejected  = "stock.rejected"
)

// CartCheckedOut é o "data" do evento publicado pelo Cart. Preços chegam como número decimal JSON.
type CartCheckedOut struct {
	CheckoutID string           `json:"checkoutId"`
	CartID     string           `json:"cartId"`
	BuyerID    string           `json:"buyerId"`
	Items      []CheckedOutItem `json:"items"`
}

type CheckedOutItem struct {
	ProductID   string      `json:"productId"`
	SellerID    string      `json:"sellerId"`
	ProductName string      `json:"productName"`
	UnitPrice   json.Number `json:"unitPrice"`
	Currency    string      `json:"currency"`
	Quantity    int         `json:"quantity"`
}

// OrderPlaced é o "data" do evento publicado para o Catalog baixar o estoque.
type OrderPlaced struct {
	OrderID string       `json:"orderId"`
	BuyerID string       `json:"buyerId"`
	CartID  string       `json:"cartId"`
	Items   []PlacedItem `json:"items"`
}

type PlacedItem struct {
	ProductID string `json:"productId"`
	Quantity  int    `json:"quantity"`
}

// StockReserved: o Catalog baixou o estoque de todos os itens.
type StockReserved struct {
	OrderID string `json:"orderId"`
}

// StockRejected: faltou estoque (nada foi baixado); Reason é legível para o comprador.
type StockRejected struct {
	OrderID    string   `json:"orderId"`
	Reason     string   `json:"reason"`
	ProductIDs []string `json:"productIds"`
}

// MaxOrdersListed limita "meus pedidos" (paginação fica para depois).
const MaxOrdersListed = 50

// Service reúne os casos de uso.
type Service struct {
	repo  Repository
	pub   Publisher
	clock func() time.Time
	stats metrics
}

func NewService(repo Repository, pub Publisher, clock func() time.Time) *Service {
	return &Service{repo: repo, pub: pub, clock: clock, stats: newMetrics()}
}

// PlaceOrder cria o pedido a partir do checkout (id do pedido = checkoutId) e publica order.placed.
// Idempotente: checkout repetido não cria outro pedido, mas republica order.placed.
func (s *Service) PlaceOrder(ctx context.Context, event CartCheckedOut) (*order.Order, error) {
	items := make([]order.Item, 0, len(event.Items))
	for _, i := range event.Items {
		cents, err := order.ParseCents(i.UnitPrice.String())
		if err != nil {
			return nil, fmt.Errorf("%w: %v", ErrInvalidEvent, err)
		}
		items = append(items, order.Item{
			ProductID: i.ProductID, SellerID: i.SellerID, ProductName: i.ProductName,
			UnitPrice: order.Money{Cents: cents, Currency: i.Currency}, Quantity: i.Quantity,
		})
	}

	o, err := order.New(event.CheckoutID, event.BuyerID, event.CartID, items, s.clock())
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrInvalidEvent, err)
	}

	switch err := s.repo.Insert(ctx, o); {
	case errors.Is(err, ErrDuplicate):
		if o, err = s.repo.Get(ctx, event.CheckoutID); err != nil {
			return nil, fmt.Errorf("load duplicated order: %w", err)
		}
	case err != nil:
		return nil, fmt.Errorf("store order: %w", err)
	default:
		s.stats.placed(ctx)
	}

	if err := s.pub.Publish(ctx, EventOrderPlaced, o.ID, o.ID, placedEvent(o)); err != nil {
		return nil, fmt.Errorf("publish %s: %w", EventOrderPlaced, err)
	}
	return o, nil
}

func placedEvent(o *order.Order) OrderPlaced {
	items := make([]PlacedItem, len(o.Items))
	for i, item := range o.Items {
		items[i] = PlacedItem{ProductID: item.ProductID, Quantity: item.Quantity}
	}
	return OrderPlaced{OrderID: o.ID, BuyerID: o.BuyerID, CartID: o.CartID, Items: items}
}

// ConfirmOrder marca o pedido como confirmado (estoque reservado). Repetir é inofensivo.
func (s *Service) ConfirmOrder(ctx context.Context, event StockReserved) error {
	return s.transition(ctx, event.OrderID, func(o *order.Order) error { return o.Confirm(s.clock()) })
}

// RejectOrder recusa o pedido com o motivo vindo do Catalog. Repetir é inofensivo.
func (s *Service) RejectOrder(ctx context.Context, event StockRejected) error {
	return s.transition(ctx, event.OrderID, func(o *order.Order) error { return o.Reject(event.Reason, s.clock()) })
}

// transition carrega, aplica a regra do domínio e grava. Pedido inexistente ou transição proibida nunca vai dar
// certo tentando de novo → ErrInvalidEvent (o consumidor registra e pula).
func (s *Service) transition(ctx context.Context, orderID string, apply func(*order.Order) error) error {
	o, err := s.repo.Get(ctx, orderID)
	if errors.Is(err, ErrNotFound) {
		return fmt.Errorf("%w: order %s not found", ErrInvalidEvent, orderID)
	}
	if err != nil {
		return fmt.Errorf("load order: %w", err)
	}
	before := o.Status
	if err := apply(o); err != nil {
		return fmt.Errorf("%w: %v", ErrInvalidEvent, err)
	}
	if o.Status == before {
		return nil // evento repetido: nada mudou
	}
	if err := s.repo.UpdateStatus(ctx, o); err != nil {
		return fmt.Errorf("update order: %w", err)
	}
	s.stats.resolved(ctx, o, s.clock())
	return nil
}

// OrdersOf lista os pedidos do comprador, mais recentes primeiro.
func (s *Service) OrdersOf(ctx context.Context, buyerID string) ([]*order.Order, error) {
	return s.repo.ListByBuyer(ctx, buyerID, MaxOrdersListed)
}

// OrderOf devolve o pedido só se for do comprador (senão ErrNotFound, para não revelar pedidos alheios).
func (s *Service) OrderOf(ctx context.Context, buyerID, id string) (*order.Order, error) {
	o, err := s.repo.Get(ctx, id)
	if err != nil {
		return nil, err
	}
	if o.BuyerID != buyerID {
		return nil, ErrNotFound
	}
	return o, nil
}
