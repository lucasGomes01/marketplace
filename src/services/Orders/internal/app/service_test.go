package app

import (
	"context"
	"encoding/json"
	"errors"
	"sort"
	"testing"
	"time"

	"marketplace/orders/internal/order"
)

var now = time.Date(2026, 10, 13, 12, 0, 0, 0, time.UTC)

// memoryRepo imita o store: pedido e evento são gravados juntos (ou nenhum dos dois) — o outbox em memória.
type memoryRepo struct {
	orders          map[string]*order.Order
	outbox          []Event
	shipmentWrites  int
	conflictOnWrite bool
	failWrites      bool
}

func newMemoryRepo() *memoryRepo { return &memoryRepo{orders: map[string]*order.Order{}} }

// clone copia também os envios: o repositório não pode compartilhar o slice com quem chamou.
func clone(o *order.Order) *order.Order {
	copied := *o
	copied.Shipments = append([]order.Shipment(nil), o.Shipments...)
	return &copied
}

func (r *memoryRepo) Insert(_ context.Context, o *order.Order, event Event) error {
	if r.failWrites {
		return errors.New("database down")
	}
	if _, exists := r.orders[o.ID]; exists {
		return ErrDuplicate
	}
	r.orders[o.ID] = clone(o)
	r.outbox = append(r.outbox, event)
	return nil
}

func (r *memoryRepo) UpdateStatus(_ context.Context, o *order.Order) error {
	if _, ok := r.orders[o.ID]; !ok {
		return ErrNotFound
	}
	r.orders[o.ID] = clone(o)
	return nil
}

func (r *memoryRepo) UpdateShipment(_ context.Context, o *order.Order, sellerID string, previous order.ShipmentStatus, event Event) error {
	stored, ok := r.orders[o.ID]
	if !ok {
		return ErrNotFound
	}
	if current, _ := stored.Shipment(sellerID); r.conflictOnWrite || current.Status != previous {
		return ErrConflict
	}
	r.shipmentWrites++
	r.orders[o.ID] = clone(o)
	r.outbox = append(r.outbox, event)
	return nil
}

func (r *memoryRepo) ListBySeller(_ context.Context, sellerID string, limit int) ([]*order.Order, error) {
	var list []*order.Order
	for _, o := range r.orders {
		if _, ok := o.Shipment(sellerID); ok && o.Status == order.StatusConfirmed {
			list = append(list, clone(o))
		}
	}
	sort.Slice(list, func(i, j int) bool { return list[i].CreatedAt.After(list[j].CreatedAt) })
	if len(list) > limit {
		list = list[:limit]
	}
	return list, nil
}

func (r *memoryRepo) Get(_ context.Context, id string) (*order.Order, error) {
	if o, ok := r.orders[id]; ok {
		return clone(o), nil
	}
	return nil, ErrNotFound
}

func (r *memoryRepo) ListByBuyer(_ context.Context, buyerID string, limit int) ([]*order.Order, error) {
	var list []*order.Order
	for _, o := range r.orders {
		if o.BuyerID == buyerID {
			list = append(list, o)
		}
	}
	sort.Slice(list, func(i, j int) bool { return list[i].CreatedAt.After(list[j].CreatedAt) })
	if len(list) > limit {
		list = list[:limit]
	}
	return list, nil
}

func newService() (*Service, *memoryRepo) {
	repo := newMemoryRepo()
	return NewService(repo, func() time.Time { return now }), repo
}

func checkedOut(checkoutID string) CartCheckedOut {
	return CartCheckedOut{
		CheckoutID: checkoutID,
		CartID:     "cart-1",
		BuyerID:    "buyer-1",
		Items: []CheckedOutItem{
			{ProductID: "p-drill", SellerID: "s-ana", ProductName: "Furadeira", UnitPrice: json.Number("199.90"), Currency: "BRL", Quantity: 2},
			{ProductID: "p-wrench", SellerID: "s-bia", ProductName: "Chaves", UnitPrice: json.Number("89.5"), Currency: "BRL", Quantity: 1},
		},
	}
}

func TestPlaceOrderStoresAPendingOrderTogetherWithOrderPlaced(t *testing.T) {
	svc, repo := newService()

	o, err := svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if o.ID != "order-1" || o.Status != order.StatusPending || o.Total().Cents != 48930 {
		t.Errorf("order = %+v (total %d)", o, o.Total().Cents)
	}
	if _, ok := repo.orders["order-1"]; !ok {
		t.Error("order was not stored")
	}
	if len(repo.outbox) != 1 {
		t.Fatalf("recorded %d events, want 1", len(repo.outbox))
	}
	sent := repo.outbox[0]
	placed, ok := sent.Data.(OrderPlaced)
	if sent.Type != "order.placed" || sent.Key != "order-1" || sent.ID != "order-1" || !ok {
		t.Fatalf("recorded %+v", sent)
	}
	if placed.OrderID != "order-1" || placed.BuyerID != "buyer-1" || len(placed.Items) != 2 ||
		placed.Items[0] != (PlacedItem{ProductID: "p-drill", Quantity: 2}) {
		t.Errorf("order.placed = %+v", placed)
	}
}

func TestADuplicateCheckoutCreatesNeitherASecondOrderNorASecondEvent(t *testing.T) {
	// O order.placed da primeira vez já está no outbox (publicado ou pendente).
	svc, repo := newService()
	_, _ = svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	again, err := svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	if err != nil || again.ID != "order-1" {
		t.Fatalf("order = %+v, err = %v", again, err)
	}
	if len(repo.orders) != 1 {
		t.Errorf("stored %d orders, want 1", len(repo.orders))
	}
	if len(repo.outbox) != 1 {
		t.Errorf("recorded %d events, want 1", len(repo.outbox))
	}
}

func TestAnInvalidEventIsReportedAsInvalid(t *testing.T) {
	svc, repo := newService()
	badPrice := checkedOut("order-1")
	badPrice.Items[0].UnitPrice = json.Number("1.999")
	noItems := checkedOut("order-2")
	noItems.Items = nil

	for _, event := range []CartCheckedOut{badPrice, noItems} {
		if _, err := svc.PlaceOrder(context.Background(), event); !errors.Is(err, ErrInvalidEvent) {
			t.Errorf("err = %v, want ErrInvalidEvent", err)
		}
	}
	if len(repo.orders) != 0 || len(repo.outbox) != 0 {
		t.Errorf("nothing should be stored or recorded")
	}
}

func TestAStoreFailureIsReturnedSoTheEventIsRetried(t *testing.T) {
	svc, repo := newService()
	repo.failWrites = true

	_, err := svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	if err == nil || errors.Is(err, ErrInvalidEvent) {
		t.Fatalf("err = %v, want a retryable error", err)
	}
	if len(repo.orders) != 0 || len(repo.outbox) != 0 {
		t.Error("neither the order nor the event may be stored")
	}
}

func TestBuyersOnlySeeTheirOwnOrders(t *testing.T) {
	svc, _ := newService()
	_, _ = svc.PlaceOrder(context.Background(), checkedOut("order-1"))
	other := checkedOut("order-2")
	other.BuyerID = "buyer-2"
	_, _ = svc.PlaceOrder(context.Background(), other)

	mine, _ := svc.OrdersOf(context.Background(), "buyer-1")
	_, errOthers := svc.OrderOf(context.Background(), "buyer-1", "order-2")
	own, errOwn := svc.OrderOf(context.Background(), "buyer-1", "order-1")

	if len(mine) != 1 || mine[0].ID != "order-1" {
		t.Errorf("OrdersOf = %+v", mine)
	}
	if !errors.Is(errOthers, ErrNotFound) {
		t.Errorf("another buyer's order: err = %v, want ErrNotFound", errOthers)
	}
	if errOwn != nil || own.ID != "order-1" {
		t.Errorf("own order: %+v, %v", own, errOwn)
	}
}

func TestStockReservedConfirmsTheOrder(t *testing.T) {
	svc, repo := newService()
	_, _ = svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	if err := svc.ConfirmOrder(context.Background(), StockReserved{OrderID: "order-1"}); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if got := repo.orders["order-1"]; got.Status != order.StatusConfirmed || !got.UpdatedAt.Equal(now) {
		t.Errorf("order = %+v", got)
	}
}

func TestStockRejectedRejectsTheOrderWithTheReason(t *testing.T) {
	svc, repo := newService()
	_, _ = svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	err := svc.RejectOrder(context.Background(), StockRejected{OrderID: "order-1", Reason: "Estoque insuficiente: Furadeira", ProductIDs: []string{"p-drill"}})

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if got := repo.orders["order-1"]; got.Status != order.StatusRejected || got.RejectionReason != "Estoque insuficiente: Furadeira" {
		t.Errorf("order = %+v", got)
	}
}

func TestRepeatedStockEventsAreHarmless(t *testing.T) {
	svc, repo := newService()
	_, _ = svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	for range 2 {
		if err := svc.ConfirmOrder(context.Background(), StockReserved{OrderID: "order-1"}); err != nil {
			t.Fatalf("unexpected error: %v", err)
		}
	}

	if repo.orders["order-1"].Status != order.StatusConfirmed {
		t.Errorf("status = %q", repo.orders["order-1"].Status)
	}
}

func TestStockEventForAnUnknownOrIncompatibleOrderIsInvalid(t *testing.T) {
	svc, _ := newService()
	_, _ = svc.PlaceOrder(context.Background(), checkedOut("order-1"))
	_ = svc.ConfirmOrder(context.Background(), StockReserved{OrderID: "order-1"})

	unknown := svc.ConfirmOrder(context.Background(), StockReserved{OrderID: "nope"})
	conflicting := svc.RejectOrder(context.Background(), StockRejected{OrderID: "order-1", Reason: "tarde demais"})

	if !errors.Is(unknown, ErrInvalidEvent) {
		t.Errorf("unknown order: err = %v, want ErrInvalidEvent", unknown)
	}
	if !errors.Is(conflicting, ErrInvalidEvent) {
		t.Errorf("reject after confirm: err = %v, want ErrInvalidEvent", conflicting)
	}
}
