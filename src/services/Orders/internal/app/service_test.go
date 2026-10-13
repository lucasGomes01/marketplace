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

type memoryRepo struct{ orders map[string]*order.Order }

func newMemoryRepo() *memoryRepo { return &memoryRepo{orders: map[string]*order.Order{}} }

func (r *memoryRepo) Insert(_ context.Context, o *order.Order) error {
	if _, exists := r.orders[o.ID]; exists {
		return ErrDuplicate
	}
	copied := *o
	r.orders[o.ID] = &copied
	return nil
}

func (r *memoryRepo) Get(_ context.Context, id string) (*order.Order, error) {
	if o, ok := r.orders[id]; ok {
		copied := *o
		return &copied, nil
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

type published struct {
	eventType, key, eventID string
	data                    any
}

type fakePublisher struct {
	sent []published
	fail bool
}

func (p *fakePublisher) Publish(_ context.Context, eventType, key, eventID string, data any) error {
	if p.fail {
		return errors.New("broker down")
	}
	p.sent = append(p.sent, published{eventType, key, eventID, data})
	return nil
}

func newService() (*Service, *memoryRepo, *fakePublisher) {
	repo, pub := newMemoryRepo(), &fakePublisher{}
	return NewService(repo, pub, func() time.Time { return now }), repo, pub
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

func TestPlaceOrderStoresAPendingOrderAndPublishesOrderPlaced(t *testing.T) {
	svc, repo, pub := newService()

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
	if len(pub.sent) != 1 {
		t.Fatalf("published %d events, want 1", len(pub.sent))
	}
	sent := pub.sent[0]
	placed, ok := sent.data.(OrderPlaced)
	if sent.eventType != "order.placed" || sent.key != "order-1" || sent.eventID != "order-1" || !ok {
		t.Fatalf("published %+v", sent)
	}
	if placed.OrderID != "order-1" || placed.BuyerID != "buyer-1" || len(placed.Items) != 2 ||
		placed.Items[0] != (PlacedItem{ProductID: "p-drill", Quantity: 2}) {
		t.Errorf("order.placed = %+v", placed)
	}
}

func TestADuplicateCheckoutDoesNotCreateASecondOrderButRepublishes(t *testing.T) {
	svc, repo, pub := newService()
	_, _ = svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	again, err := svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	if err != nil || again.ID != "order-1" {
		t.Fatalf("order = %+v, err = %v", again, err)
	}
	if len(repo.orders) != 1 {
		t.Errorf("stored %d orders, want 1", len(repo.orders))
	}
	if len(pub.sent) != 2 || pub.sent[1].eventID != "order-1" {
		t.Errorf("expected order.placed to be republished with the same id, got %+v", pub.sent)
	}
}

func TestAnInvalidEventIsReportedAsInvalid(t *testing.T) {
	svc, repo, pub := newService()
	badPrice := checkedOut("order-1")
	badPrice.Items[0].UnitPrice = json.Number("1.999")
	noItems := checkedOut("order-2")
	noItems.Items = nil

	for _, event := range []CartCheckedOut{badPrice, noItems} {
		if _, err := svc.PlaceOrder(context.Background(), event); !errors.Is(err, ErrInvalidEvent) {
			t.Errorf("err = %v, want ErrInvalidEvent", err)
		}
	}
	if len(repo.orders) != 0 || len(pub.sent) != 0 {
		t.Errorf("nothing should be stored or published")
	}
}

func TestPublishFailureIsReturnedSoTheEventIsRetried(t *testing.T) {
	svc, repo, pub := newService()
	pub.fail = true

	_, err := svc.PlaceOrder(context.Background(), checkedOut("order-1"))

	if err == nil || errors.Is(err, ErrInvalidEvent) {
		t.Fatalf("err = %v, want a retryable error", err)
	}
	if _, ok := repo.orders["order-1"]; !ok {
		t.Error("order should be stored even if publishing failed (the retry republishes)")
	}
}

func TestBuyersOnlySeeTheirOwnOrders(t *testing.T) {
	svc, _, _ := newService()
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
