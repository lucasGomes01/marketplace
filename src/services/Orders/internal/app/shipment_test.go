package app

import (
	"context"
	"errors"
	"testing"

	"marketplace/orders/internal/order"
)

// confirmedOrder cria order-1 (itens da s-ana e da s-bia, comprador buyer-1) já confirmado.
func confirmedOrder(t *testing.T, svc *Service, id string) {
	t.Helper()
	ctx := context.Background()
	if _, err := svc.PlaceOrder(ctx, checkedOut(id)); err != nil {
		t.Fatal(err)
	}
	if err := svc.ConfirmOrder(ctx, StockReserved{OrderID: id}); err != nil {
		t.Fatal(err)
	}
}

func lastPublished(t *testing.T, pub *fakePublisher) published {
	t.Helper()
	if len(pub.sent) == 0 {
		t.Fatal("nothing published")
	}
	return pub.sent[len(pub.sent)-1]
}

func TestSellerAdvancesTheirShipmentAndAnEventIsPublished(t *testing.T) {
	svc, repo, pub := newService()
	confirmedOrder(t, svc, "order-1")

	o, err := svc.AdvanceShipment(context.Background(), "s-ana", "order-1", order.ShipmentPicked)

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if s, _ := o.Shipment("s-ana"); s.Status != order.ShipmentPicked || !s.PickedAt.Equal(now) {
		t.Errorf("returned shipment = %+v", s)
	}
	if s, _ := repo.orders["order-1"].Shipment("s-ana"); s.Status != order.ShipmentPicked {
		t.Errorf("stored shipment = %+v", s)
	}
	sent := lastPublished(t, pub)
	event, ok := sent.data.(ShipmentStatusChanged)
	if sent.eventType != "shipment.status-changed" || sent.key != "order-1" || sent.eventID != "order-1:s-ana:Picked" || !ok {
		t.Fatalf("published %+v", sent)
	}
	want := ShipmentStatusChanged{OrderID: "order-1", BuyerID: "buyer-1", SellerID: "s-ana", Status: order.ShipmentPicked, ChangedAt: now}
	if event != want {
		t.Errorf("event = %+v, want %+v", event, want)
	}
}

func TestRepeatingTheSameStatusRepublishesWithoutWritingAgain(t *testing.T) {
	svc, repo, pub := newService()
	confirmedOrder(t, svc, "order-1")
	_, _ = svc.AdvanceShipment(context.Background(), "s-ana", "order-1", order.ShipmentPicked)
	before := len(pub.sent)

	if _, err := svc.AdvanceShipment(context.Background(), "s-ana", "order-1", order.ShipmentPicked); err != nil {
		t.Fatalf("repeat: %v", err)
	}

	if repo.shipmentWrites != 1 {
		t.Errorf("writes = %d, want 1", repo.shipmentWrites)
	}
	if len(pub.sent) != before+1 || lastPublished(t, pub).eventID != "order-1:s-ana:Picked" {
		t.Errorf("expected the same event to be republished, got %+v", pub.sent[before:])
	}
}

func TestShipmentErrorsAreReported(t *testing.T) {
	svc, repo, _ := newService()
	confirmedOrder(t, svc, "order-1")
	_, _ = svc.PlaceOrder(context.Background(), checkedOut("order-pending"))

	cases := []struct {
		name            string
		seller, orderID string
		to              order.ShipmentStatus
		want            error
	}{
		{"unknown order", "s-ana", "nope", order.ShipmentPicked, ErrNotFound},
		{"seller without items in the order", "s-carla", "order-1", order.ShipmentPicked, ErrNotFound},
		{"skipping a step", "s-ana", "order-1", order.ShipmentShipped, order.ErrInvalidTransition},
		{"order not confirmed", "s-ana", "order-pending", order.ShipmentPicked, order.ErrInvalidTransition},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			_, err := svc.AdvanceShipment(context.Background(), tc.seller, tc.orderID, tc.to)
			if !errors.Is(err, tc.want) {
				t.Errorf("err = %v, want %v", err, tc.want)
			}
		})
	}
	if repo.shipmentWrites != 0 {
		t.Errorf("writes = %d, want 0", repo.shipmentWrites)
	}
}

func TestAConcurrentChangeIsAConflictAndNothingIsPublished(t *testing.T) {
	svc, repo, pub := newService()
	confirmedOrder(t, svc, "order-1")
	repo.conflictOnWrite = true
	before := len(pub.sent)

	_, err := svc.AdvanceShipment(context.Background(), "s-ana", "order-1", order.ShipmentPicked)

	if !errors.Is(err, ErrConflict) {
		t.Errorf("err = %v, want ErrConflict", err)
	}
	if len(pub.sent) != before {
		t.Errorf("published %d events on conflict", len(pub.sent)-before)
	}
}

func TestBuyerConfirmsDeliveryOfTheirOwnOrder(t *testing.T) {
	svc, _, pub := newService()
	confirmedOrder(t, svc, "order-1")
	ctx := context.Background()
	for _, step := range []order.ShipmentStatus{order.ShipmentPicked, order.ShipmentPacked, order.ShipmentShipped} {
		if _, err := svc.AdvanceShipment(ctx, "s-ana", "order-1", step); err != nil {
			t.Fatal(err)
		}
	}

	if _, err := svc.ConfirmDelivery(ctx, "buyer-2", "order-1", "s-ana"); !errors.Is(err, ErrNotFound) {
		t.Errorf("another buyer: err = %v, want ErrNotFound", err)
	}
	o, err := svc.ConfirmDelivery(ctx, "buyer-1", "order-1", "s-ana")

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if s, _ := o.Shipment("s-ana"); s.Status != order.ShipmentDelivered {
		t.Errorf("shipment = %+v", s)
	}
	if got := lastPublished(t, pub); got.eventID != "order-1:s-ana:Delivered" {
		t.Errorf("published %+v", got)
	}
	if _, err := svc.ConfirmDelivery(ctx, "buyer-1", "order-1", "s-bia"); !errors.Is(err, order.ErrInvalidTransition) {
		t.Errorf("not shipped yet: err = %v, want ErrInvalidTransition", err)
	}
}

func TestSellersSeeOnlyConfirmedOrdersWithTheirShipment(t *testing.T) {
	svc, _, _ := newService()
	confirmedOrder(t, svc, "order-1")
	_, _ = svc.PlaceOrder(context.Background(), checkedOut("order-pending"))

	ana, _ := svc.ShipmentsOf(context.Background(), "s-ana")
	carla, _ := svc.ShipmentsOf(context.Background(), "s-carla")

	if len(ana) != 1 || ana[0].ID != "order-1" {
		t.Errorf("s-ana sees %v", ana)
	}
	if len(carla) != 0 {
		t.Errorf("s-carla sees %v", carla)
	}
}
