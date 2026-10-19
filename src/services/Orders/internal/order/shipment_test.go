package order

import (
	"errors"
	"testing"
	"time"
)

func confirmedOrder(t *testing.T) *Order {
	t.Helper()
	o := newValidOrder(t)
	if err := o.Confirm(now); err != nil {
		t.Fatalf("confirm: %v", err)
	}
	return o
}

func shipmentOf(t *testing.T, o *Order, sellerID string) Shipment {
	t.Helper()
	s, ok := o.Shipment(sellerID)
	if !ok {
		t.Fatalf("no shipment for %s", sellerID)
	}
	return s
}

func TestNewCreatesOneShipmentPerSellerInItemOrder(t *testing.T) {
	items := append(validItems(), Item{ProductID: "p-saw", SellerID: "s-ana", ProductName: "Serra", UnitPrice: brl(5000), Quantity: 1})
	o, err := New("order-1", "buyer-1", "cart-1", items, now)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if len(o.Shipments) != 2 {
		t.Fatalf("shipments = %d, want 2 (one per seller)", len(o.Shipments))
	}
	for i, seller := range []string{"s-ana", "s-bia"} {
		s := o.Shipments[i]
		if s.SellerID != seller || s.Status != ShipmentAwaitingPicking {
			t.Errorf("shipment %d = %s/%s, want %s/AwaitingPicking", i, s.SellerID, s.Status, seller)
		}
	}
}

func TestSellerAdvancesStepByStepUntilShipped(t *testing.T) {
	o := confirmedOrder(t)
	steps := []ShipmentStatus{ShipmentPicked, ShipmentPacked, ShipmentShipped}

	for i, step := range steps {
		at := now.Add(time.Duration(i+1) * time.Hour)
		if err := o.AdvanceShipment("s-ana", step, at); err != nil {
			t.Fatalf("advance to %s: %v", step, err)
		}
		if got := shipmentOf(t, o, "s-ana").Status; got != step {
			t.Fatalf("status = %s, want %s", got, step)
		}
		if !o.UpdatedAt.Equal(at) {
			t.Errorf("order UpdatedAt = %v, want %v", o.UpdatedAt, at)
		}
	}

	s := shipmentOf(t, o, "s-ana")
	if !s.PickedAt.Equal(now.Add(time.Hour)) || !s.PackedAt.Equal(now.Add(2*time.Hour)) || !s.ShippedAt.Equal(now.Add(3*time.Hour)) {
		t.Errorf("timeline = %v / %v / %v", s.PickedAt, s.PackedAt, s.ShippedAt)
	}
	if !s.DeliveredAt.IsZero() {
		t.Errorf("DeliveredAt = %v, want zero", s.DeliveredAt)
	}
	if other := shipmentOf(t, o, "s-bia"); other.Status != ShipmentAwaitingPicking {
		t.Errorf("other seller's shipment = %s, want untouched", other.Status)
	}
}

func TestSellerCannotSkipGoBackOrDeliver(t *testing.T) {
	cases := []struct {
		name string
		from []ShipmentStatus
		to   ShipmentStatus
	}{
		{"skip picking", nil, ShipmentPacked},
		{"skip to shipped", []ShipmentStatus{ShipmentPicked}, ShipmentShipped},
		{"go back", []ShipmentStatus{ShipmentPicked, ShipmentPacked}, ShipmentPicked},
		{"back to awaiting", []ShipmentStatus{ShipmentPicked}, ShipmentAwaitingPicking},
		{"seller marks delivered", []ShipmentStatus{ShipmentPicked, ShipmentPacked, ShipmentShipped}, ShipmentDelivered},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			o := confirmedOrder(t)
			for _, step := range tc.from {
				if err := o.AdvanceShipment("s-ana", step, now); err != nil {
					t.Fatalf("setup %s: %v", step, err)
				}
			}
			before := shipmentOf(t, o, "s-ana")

			err := o.AdvanceShipment("s-ana", tc.to, now.Add(time.Hour))

			if !errors.Is(err, ErrInvalidTransition) {
				t.Fatalf("err = %v, want ErrInvalidTransition", err)
			}
			if after := shipmentOf(t, o, "s-ana"); after != before {
				t.Errorf("shipment changed on error: %+v", after)
			}
		})
	}
}

func TestUnknownShipmentStatusIsAValidationError(t *testing.T) {
	o := confirmedOrder(t)

	err := o.AdvanceShipment("s-ana", ShipmentStatus("Lost"), now)

	if fieldOf(t, err) != "status" {
		t.Errorf("field = %q, want status", fieldOf(t, err))
	}
}

func TestRepeatingTheCurrentStatusChangesNothing(t *testing.T) {
	o := confirmedOrder(t)
	_ = o.AdvanceShipment("s-ana", ShipmentPicked, now.Add(time.Hour))

	if err := o.AdvanceShipment("s-ana", ShipmentPicked, now.Add(2*time.Hour)); err != nil {
		t.Fatalf("repeat: %v", err)
	}

	if s := shipmentOf(t, o, "s-ana"); !s.PickedAt.Equal(now.Add(time.Hour)) {
		t.Errorf("PickedAt = %v, want the first time", s.PickedAt)
	}
	if !o.UpdatedAt.Equal(now.Add(time.Hour)) {
		t.Errorf("UpdatedAt = %v, want unchanged", o.UpdatedAt)
	}
}

func TestOnlyConfirmedOrdersAreShipped(t *testing.T) {
	pending := newValidOrder(t)
	rejected := newValidOrder(t)
	_ = rejected.Reject("Estoque insuficiente", now)

	for _, o := range []*Order{pending, rejected} {
		if err := o.AdvanceShipment("s-ana", ShipmentPicked, now); !errors.Is(err, ErrInvalidTransition) {
			t.Errorf("%s order: err = %v, want ErrInvalidTransition", o.Status, err)
		}
	}
}

func TestSellerWithoutItemsInTheOrderHasNoShipment(t *testing.T) {
	o := confirmedOrder(t)

	if err := o.AdvanceShipment("s-carla", ShipmentPicked, now); !errors.Is(err, ErrShipmentNotFound) {
		t.Errorf("advance: err = %v, want ErrShipmentNotFound", err)
	}
	if err := o.ConfirmDelivery("s-carla", now); !errors.Is(err, ErrShipmentNotFound) {
		t.Errorf("deliver: err = %v, want ErrShipmentNotFound", err)
	}
}

func TestBuyerConfirmsDeliveryOfAShippedShipment(t *testing.T) {
	o := confirmedOrder(t)
	for _, step := range []ShipmentStatus{ShipmentPicked, ShipmentPacked, ShipmentShipped} {
		_ = o.AdvanceShipment("s-ana", step, now)
	}
	at := now.Add(48 * time.Hour)

	if err := o.ConfirmDelivery("s-ana", at); err != nil {
		t.Fatalf("deliver: %v", err)
	}
	if err := o.ConfirmDelivery("s-ana", at.Add(time.Hour)); err != nil {
		t.Fatalf("repeat: %v", err)
	}

	s := shipmentOf(t, o, "s-ana")
	if s.Status != ShipmentDelivered || !s.DeliveredAt.Equal(at) {
		t.Errorf("shipment = %s at %v, want Delivered at %v", s.Status, s.DeliveredAt, at)
	}
	if err := o.AdvanceShipment("s-ana", ShipmentShipped, at); !errors.Is(err, ErrInvalidTransition) {
		t.Errorf("seller after delivery: err = %v, want ErrInvalidTransition", err)
	}
}

func TestBuyerCannotConfirmDeliveryBeforeShipping(t *testing.T) {
	o := confirmedOrder(t)
	_ = o.AdvanceShipment("s-ana", ShipmentPicked, now)

	if err := o.ConfirmDelivery("s-ana", now); !errors.Is(err, ErrInvalidTransition) {
		t.Errorf("err = %v, want ErrInvalidTransition", err)
	}
}
