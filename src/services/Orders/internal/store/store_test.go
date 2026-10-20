package store

import (
	"context"
	"encoding/json"
	"errors"
	"net/url"
	"sync"
	"testing"
	"time"

	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/modules/mssql"
	"github.com/testcontainers/testcontainers-go/wait"
	"go.opentelemetry.io/otel/trace"

	"marketplace/orders/internal/app"
	"marketplace/orders/internal/order"
	"marketplace/orders/internal/outbox"
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
		testcontainers.WithWaitStrategyAndDeadline(4*time.Minute, wait.ForLog("Recovery is complete").WithStartupTimeout(4*time.Minute)))
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
		if err := s.Insert(ctx, o, placed(o)); err != nil {
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
		again := newOrder(t, "order-1", "buyer-1", base)
		if err := s.Insert(ctx, again, placed(again)); !errors.Is(err, app.ErrDuplicate) {
			t.Errorf("err = %v, want ErrDuplicate", err)
		}
	})

	t.Run("the event is stored with the order and a duplicate stores no second event", func(t *testing.T) {
		events := pendingByID(t, s, "order-1")
		if len(events) != 1 {
			t.Fatalf("outbox has %d messages for order-1, want 1", len(events))
		}
		m := events[0]
		var env map[string]any
		_ = json.Unmarshal(m.Payload, &env)
		if m.Type != "order.placed" || m.Key != "order-1" || env["eventId"] != "order-1" || env["type"] != "order.placed" ||
			env["data"].(map[string]any)["orderId"] != "order-1" {
			t.Errorf("message = %+v, envelope = %s", m, m.Payload)
		}
	})

	t.Run("the traceparent of the request is stored with the event", func(t *testing.T) {
		traceID, _ := trace.TraceIDFromHex("0af7651916cd43dd8448eb211c80319c")
		spanID, _ := trace.SpanIDFromHex("b7ad6b7169203331")
		traced := trace.ContextWithSpanContext(ctx, trace.NewSpanContext(trace.SpanContextConfig{TraceID: traceID, SpanID: spanID, TraceFlags: trace.FlagsSampled}))
		o := newOrder(t, "order-traced", "buyer-5", base)

		if err := s.Insert(traced, o, placed(o)); err != nil {
			t.Fatal(err)
		}

		if m := pendingByID(t, s, "order-traced"); len(m) != 1 || m[0].TraceParent != "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" {
			t.Errorf("messages = %+v", m)
		}
	})

	t.Run("missing order is ErrNotFound", func(t *testing.T) {
		if _, err := s.Get(ctx, "nope"); !errors.Is(err, app.ErrNotFound) {
			t.Errorf("err = %v, want ErrNotFound", err)
		}
	})

	t.Run("updates status, reason and updatedAt", func(t *testing.T) {
		o := newOrder(t, "order-status", "buyer-3", base)
		if err := s.Insert(ctx, o, placed(o)); err != nil {
			t.Fatal(err)
		}
		_ = o.Reject("Estoque insuficiente: Furadeira", base.Add(time.Minute))

		if err := s.UpdateStatus(ctx, o); err != nil {
			t.Fatalf("update: %v", err)
		}

		got, _ := s.Get(ctx, "order-status")
		if got.Status != order.StatusRejected || got.RejectionReason != "Estoque insuficiente: Furadeira" || !got.UpdatedAt.Equal(base.Add(time.Minute)) {
			t.Errorf("order = %+v", got)
		}
		if err := s.UpdateStatus(ctx, newOrder(t, "missing", "x", base)); !errors.Is(err, app.ErrNotFound) {
			t.Errorf("missing order: err = %v, want ErrNotFound", err)
		}
	})

	t.Run("shipments are stored with the order and read in seller order", func(t *testing.T) {
		got, _ := s.Get(ctx, "order-1")
		if len(got.Shipments) != 2 || got.Shipments[0].SellerID != "s-ana" || got.Shipments[1].SellerID != "s-bia" {
			t.Fatalf("shipments = %+v", got.Shipments)
		}
		if got.Shipments[0].Status != order.ShipmentAwaitingPicking || !got.Shipments[0].PickedAt.IsZero() {
			t.Errorf("new shipment = %+v", got.Shipments[0])
		}
	})

	t.Run("updates a shipment only if it is still in the expected status", func(t *testing.T) {
		o := newOrder(t, "order-ship", "buyer-4", base)
		if err := s.Insert(ctx, o, placed(o)); err != nil {
			t.Fatal(err)
		}
		_ = o.Confirm(base)
		_ = s.UpdateStatus(ctx, o)
		picked := base.Add(time.Hour)
		_ = o.AdvanceShipment("s-ana", order.ShipmentPicked, picked)

		if err := s.UpdateShipment(ctx, o, "s-ana", order.ShipmentAwaitingPicking, shipped(o, "s-ana")); err != nil {
			t.Fatalf("update: %v", err)
		}
		// Segunda gravação esperando o status antigo: alguém já mudou → conflito.
		if err := s.UpdateShipment(ctx, o, "s-ana", order.ShipmentAwaitingPicking, shipped(o, "s-ana")); !errors.Is(err, app.ErrConflict) {
			t.Errorf("stale update: err = %v, want ErrConflict", err)
		}

		got, _ := s.Get(ctx, "order-ship")
		ana, _ := got.Shipment("s-ana")
		if ana.Status != order.ShipmentPicked || !ana.PickedAt.Equal(picked) || !ana.PackedAt.IsZero() {
			t.Errorf("s-ana = %+v", ana)
		}
		if bia, _ := got.Shipment("s-bia"); bia.Status != order.ShipmentAwaitingPicking {
			t.Errorf("s-bia = %+v", bia)
		}
		if !got.UpdatedAt.Equal(picked) {
			t.Errorf("UpdatedAt = %v, want %v", got.UpdatedAt, picked)
		}
		// Só a gravação que valeu registrou o evento; a do conflito não.
		if events := pendingByID(t, s, "order-ship:s-ana:Picked"); len(events) != 1 || events[0].Key != "order-ship" {
			t.Errorf("shipment events = %+v", events)
		}
	})

	t.Run("lists a seller's confirmed orders newest first", func(t *testing.T) {
		// order-ship (confirmado) tem s-ana; order-1 (pendente) não aparece.
		list, err := s.ListBySeller(ctx, "s-ana", 10)
		if err != nil {
			t.Fatal(err)
		}
		if len(list) != 1 || list[0].ID != "order-ship" || len(list[0].Items) != 2 || len(list[0].Shipments) != 2 {
			t.Errorf("list = %v", ids(list))
		}
		if none, _ := s.ListBySeller(ctx, "s-carla", 10); len(none) != 0 {
			t.Errorf("s-carla = %v", ids(none))
		}
	})

	t.Run("orders stored before shipments existed get them when the store opens", func(t *testing.T) {
		db := s.db
		if _, err := db.ExecContext(ctx, `DELETE FROM dbo.OrderShipments WHERE OrderId = 'order-1'`); err != nil {
			t.Fatal(err)
		}

		openStore(t, dsn) // o startup faz o backfill

		got, _ := s.Get(ctx, "order-1")
		if len(got.Shipments) != 2 || got.Shipments[0].SellerID != "s-ana" || got.Shipments[1].Status != order.ShipmentAwaitingPicking {
			t.Errorf("shipments after backfill = %+v", got.Shipments)
		}
	})

	t.Run("lists a buyer's orders newest first with a limit", func(t *testing.T) {
		for i, id := range []string{"b2-old", "b2-new", "b2-mid"} {
			created := base.Add(time.Duration([]int{1, 3, 2}[i]) * time.Hour)
			o := newOrder(t, id, "buyer-2", created)
			if err := s.Insert(ctx, o, placed(o)); err != nil {
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

	t.Run("a commit that stored an event wakes the relay", func(t *testing.T) {
		woken := 0
		s.WakeOnEvent(func() { woken++ })
		defer s.WakeOnEvent(nil)
		o := newOrder(t, "order-wake", "buyer-6", base)

		_ = s.Insert(ctx, o, placed(o))
		_ = s.Insert(ctx, o, placed(o)) // duplicado: nada gravado, ninguém acordado

		if woken != 1 {
			t.Errorf("woken %d times, want 1", woken)
		}
	})

	t.Run("relay operations: fail, publish, stats and cleanup", func(t *testing.T) {
		o := newOrder(t, "order-relay", "buyer-7", base)
		_ = s.Insert(ctx, o, placed(o))
		m := pendingByID(t, s, "order-relay")[0]

		if err := s.MarkFailed(ctx, m.Sequence, "broker down"); err != nil {
			t.Fatal(err)
		}
		if again := pendingByID(t, s, "order-relay"); len(again) != 1 || again[0].Attempts != 1 {
			t.Errorf("after a failure: %+v", again)
		}
		stats, err := s.Stats(ctx)
		if err != nil || stats.Pending == 0 || stats.OldestPendingSince.IsZero() {
			t.Errorf("stats = %+v, err = %v", stats, err)
		}

		published := time.Now().UTC().Add(-8 * 24 * time.Hour)
		if err := s.MarkPublished(ctx, []int64{m.Sequence}, published); err != nil {
			t.Fatal(err)
		}
		if left := pendingByID(t, s, "order-relay"); len(left) != 0 {
			t.Errorf("still pending: %+v", left)
		}
		deleted, err := s.DeletePublishedBefore(ctx, time.Now().UTC().Add(-7*24*time.Hour))
		if err != nil || deleted != 1 {
			t.Errorf("deleted = %d, err = %v", deleted, err)
		}
	})

	t.Run("only one relay per database holds the lock at a time", func(t *testing.T) {
		first, err := s.Lock().TryAcquire(ctx)
		if err != nil || first == nil {
			t.Fatalf("first: %v, %v", first, err)
		}
		second, err := s.Lock().TryAcquire(ctx)
		if err != nil || second != nil {
			t.Fatalf("second should not get the lock: %v, %v", second, err)
		}
		if !first.Held(ctx) {
			t.Error("first should still hold the lock")
		}

		_ = first.Release()
		takenOver, err := s.Lock().TryAcquire(ctx)
		if err != nil || takenOver == nil {
			t.Fatalf("after release: %v, %v", takenOver, err)
		}
		_ = takenOver.Release()
	})

	t.Run("the relay sends pending events in order and retries after a failure", func(t *testing.T) {
		drainOutbox(t, s)
		var created []string
		for _, id := range []string{"relay-a", "relay-b", "relay-c"} {
			o := newOrder(t, id, "buyer-8", base)
			if err := s.Insert(ctx, o, placed(o)); err != nil {
				t.Fatal(err)
			}
			created = append(created, id)
		}
		transport := &flakyTransport{failBatches: 1}
		relayCtx, stop := context.WithCancel(ctx)
		done := make(chan struct{})
		relay := &outbox.Relay{Store: s, Transport: transport, Lock: s.Lock(), Signal: outbox.NewSignal(), Clock: time.Now,
			Options: outbox.Options{PollInterval: 100 * time.Millisecond}}
		go func() { defer close(done); relay.Run(relayCtx) }()

		waitUntil(t, func() bool { return len(transport.ids()) == 3 })
		stop()
		<-done

		if got := transport.ids(); got[0] != created[0] || got[1] != created[1] || got[2] != created[2] {
			t.Errorf("sent %v, want %v (the failure must not let later events overtake)", got, created)
		}
		if left := pendingByID(t, s, "relay-a"); len(left) != 0 {
			t.Errorf("relay-a still pending: %+v", left)
		}
	})
}

func placed(o *order.Order) app.Event {
	return app.Event{Type: app.EventOrderPlaced, Key: o.ID, ID: o.ID, Data: map[string]string{"orderId": o.ID}}
}

func shipped(o *order.Order, sellerID string) app.Event {
	s, _ := o.Shipment(sellerID)
	return app.Event{Type: app.EventShipmentStatusChanged, Key: o.ID, ID: o.ID + ":" + sellerID + ":" + string(s.Status), Data: map[string]string{"orderId": o.ID}}
}

// pendingByID são as mensagens pendentes no outbox com o eventId pedido.
func pendingByID(t *testing.T, s *Store, eventID string) []outbox.Message {
	t.Helper()
	all, err := s.FetchPending(context.Background(), 10_000)
	if err != nil {
		t.Fatal(err)
	}
	var found []outbox.Message
	for _, m := range all {
		if m.EventID == eventID {
			found = append(found, m)
		}
	}
	return found
}

// drainOutbox marca como publicado tudo que outros subtestes deixaram pendente.
func drainOutbox(t *testing.T, s *Store) {
	t.Helper()
	all, _ := s.FetchPending(context.Background(), 10_000)
	for _, m := range all {
		_ = s.MarkPublished(context.Background(), []int64{m.Sequence}, time.Now())
	}
}

// flakyTransport simula o broker fora nos primeiros lotes (todo o lote falha) e depois aceita, registrando a ordem.
type flakyTransport struct {
	mu          sync.Mutex
	failBatches int
	sent        []string
}

func (f *flakyTransport) SendBatch(_ context.Context, batch []outbox.Message) []error {
	f.mu.Lock()
	defer f.mu.Unlock()
	errs := make([]error, len(batch))
	if f.failBatches > 0 {
		f.failBatches--
		for i := range errs {
			errs[i] = errors.New("broker down")
		}
		return errs
	}
	for _, m := range batch {
		f.sent = append(f.sent, m.EventID)
	}
	return errs
}

func (f *flakyTransport) ids() []string {
	f.mu.Lock()
	defer f.mu.Unlock()
	return append([]string(nil), f.sent...)
}

func waitUntil(t *testing.T, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(30 * time.Second)
	for !condition() {
		if time.Now().After(deadline) {
			t.Fatal("condition not met in 30s")
		}
		time.Sleep(100 * time.Millisecond)
	}
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
