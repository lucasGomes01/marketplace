package store

import (
	"context"
	"errors"
	"net/url"
	"testing"
	"time"

	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/modules/mssql"
	"github.com/testcontainers/testcontainers-go/wait"

	"marketplace/orders/internal/app"
	"marketplace/orders/internal/order"
)

// startSQLServer sobe um SQL Server real (testcontainers-go). Pulado com -short.
func startSQLServer(t *testing.T) string {
	t.Helper()
	if testing.Short() {
		t.Skip("integração: precisa de Docker (rode sem -short)")
	}
	ctx := context.Background()
	container, err := mssql.Run(ctx, "mcr.microsoft.com/mssql/server:2022-latest",
		mssql.WithAcceptEULA(), mssql.WithPassword("Orders_Test_Passw0rd!"),
		// O padrão (60s) não basta numa máquina carregada; o SQL Server pode levar mais para ficar pronto.
		testcontainers.WithWaitStrategy(wait.ForLog("Recovery is complete").WithStartupTimeout(4*time.Minute)))
	if err != nil {
		t.Fatalf("start sql server: %v", err)
	}
	t.Cleanup(func() { _ = container.Terminate(context.Background()) })

	dsn, err := container.ConnectionString(ctx)
	if err != nil {
		t.Fatal(err)
	}
	return withDatabase(t, dsn, "OrdersTest")
}

func openStore(t *testing.T, dsn string) *Store {
	t.Helper()
	s, err := Open(context.Background(), dsn)
	if err != nil {
		t.Fatalf("open store: %v", err)
	}
	t.Cleanup(func() { _ = s.Close() })
	return s
}

var base = time.Date(2026, 10, 13, 12, 0, 0, 0, time.UTC)

func newOrder(t *testing.T, id, buyer string, createdAt time.Time) *order.Order {
	t.Helper()
	o, err := order.New(id, buyer, "cart-"+id, []order.Item{
		{ProductID: "p-drill", SellerID: "s-ana", ProductName: "Furadeira de impacto", UnitPrice: order.Money{Cents: 19990, Currency: "BRL"}, Quantity: 2},
		{ProductID: "p-wrench", SellerID: "s-bia", ProductName: "Jogo de chaves (12 peças)", UnitPrice: order.Money{Cents: 8950, Currency: "BRL"}, Quantity: 1},
	}, createdAt)
	if err != nil {
		t.Fatal(err)
	}
	return o
}

func TestStoreWithRealSQLServer(t *testing.T) {
	dsn := startSQLServer(t)
	s := openStore(t, dsn)
	ctx := context.Background()

	t.Run("creates the database and schema idempotently", func(t *testing.T) {
		openStore(t, dsn) // segunda abertura não falha
	})

	t.Run("insert and get round trip", func(t *testing.T) {
		o := newOrder(t, "order-1", "buyer-1", base)
		if err := s.Insert(ctx, o); err != nil {
			t.Fatalf("insert: %v", err)
		}

		got, err := s.Get(ctx, "order-1")
		if err != nil {
			t.Fatalf("get: %v", err)
		}
		if got.BuyerID != "buyer-1" || got.CartID != "cart-order-1" || got.Status != order.StatusPending {
			t.Errorf("order = %+v", got)
		}
		if !got.CreatedAt.Equal(base) || !got.UpdatedAt.Equal(base) {
			t.Errorf("times = %v / %v", got.CreatedAt, got.UpdatedAt)
		}
		if len(got.Items) != 2 || got.Items[0] != o.Items[0] || got.Items[1] != o.Items[1] {
			t.Errorf("items = %+v, want %+v (same order, accents kept)", got.Items, o.Items)
		}
		if got.Total().Cents != 48930 {
			t.Errorf("total = %d", got.Total().Cents)
		}
	})

	t.Run("duplicate id is reported as ErrDuplicate", func(t *testing.T) {
		if err := s.Insert(ctx, newOrder(t, "order-1", "buyer-1", base)); !errors.Is(err, app.ErrDuplicate) {
			t.Errorf("err = %v, want ErrDuplicate", err)
		}
	})

	t.Run("missing order is ErrNotFound", func(t *testing.T) {
		if _, err := s.Get(ctx, "nope"); !errors.Is(err, app.ErrNotFound) {
			t.Errorf("err = %v, want ErrNotFound", err)
		}
	})

	t.Run("lists a buyer's orders newest first with a limit", func(t *testing.T) {
		for i, id := range []string{"b2-old", "b2-new", "b2-mid"} {
			created := base.Add(time.Duration([]int{1, 3, 2}[i]) * time.Hour)
			if err := s.Insert(ctx, newOrder(t, id, "buyer-2", created)); err != nil {
				t.Fatal(err)
			}
		}

		list, err := s.ListByBuyer(ctx, "buyer-2", 2)
		if err != nil {
			t.Fatal(err)
		}
		if len(list) != 2 || list[0].ID != "b2-new" || list[1].ID != "b2-mid" {
			t.Errorf("list = %v", ids(list))
		}
		if len(list[0].Items) != 2 {
			t.Errorf("items not loaded in the list: %+v", list[0])
		}
	})
}

func ids(list []*order.Order) []string {
	out := make([]string, len(list))
	for i, o := range list {
		out[i] = o.ID
	}
	return out
}

// withDatabase troca/inclui o parâmetro "database" no DSN do container.
func withDatabase(t *testing.T, dsn, name string) string {
	t.Helper()
	u, err := url.Parse(dsn)
	if err != nil {
		t.Fatal(err)
	}
	q := u.Query()
	q.Set("database", name)
	u.RawQuery = q.Encode()
	return u.String()
}
