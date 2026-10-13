package order

import (
	"errors"
	"testing"
	"time"
)

var now = time.Date(2026, 10, 13, 12, 0, 0, 0, time.UTC)

func brl(cents int64) Money { return Money{Cents: cents, Currency: "BRL"} }

func validItems() []Item {
	return []Item{
		{ProductID: "p-drill", SellerID: "s-ana", ProductName: "Furadeira", UnitPrice: brl(19990), Quantity: 2},
		{ProductID: "p-wrench", SellerID: "s-bia", ProductName: "Chaves", UnitPrice: brl(8950), Quantity: 1},
	}
}

func newValidOrder(t *testing.T) *Order {
	t.Helper()
	o, err := New("order-1", "buyer-1", "cart-1", validItems(), now)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	return o
}

func fieldOf(t *testing.T, err error) string {
	t.Helper()
	var domainErr *DomainError
	if !errors.As(err, &domainErr) {
		t.Fatalf("expected *DomainError, got %v", err)
	}
	return domainErr.Field
}

func TestNewCreatesAPendingOrderWithTotals(t *testing.T) {
	o := newValidOrder(t)

	if o.Status != StatusPending {
		t.Errorf("status = %q, want Pending", o.Status)
	}
	if got := o.Total(); got != brl(48930) {
		t.Errorf("total = %+v, want 489,30 BRL", got)
	}
	if got := o.ItemCount(); got != 3 {
		t.Errorf("item count = %d, want 3", got)
	}
	if !o.CreatedAt.Equal(now) || !o.UpdatedAt.Equal(now) {
		t.Errorf("timestamps = %v / %v, want %v", o.CreatedAt, o.UpdatedAt, now)
	}
}

func TestNewKeepsACopyOfTheItems(t *testing.T) {
	items := validItems()
	o, _ := New("order-1", "buyer-1", "cart-1", items, now)

	items[0].Quantity = 50

	if o.Items[0].Quantity != 2 {
		t.Errorf("order items changed with the caller's slice")
	}
}

func TestNewValidatesTheOrder(t *testing.T) {
	tests := []struct {
		name   string
		id     string
		buyer  string
		cart   string
		mutate func([]Item) []Item
		field  string
	}{
		{"missing id", "", "buyer-1", "cart-1", nil, "id"},
		{"missing buyer", "order-1", " ", "cart-1", nil, "buyerId"},
		{"missing cart", "order-1", "buyer-1", "", nil, "cartId"},
		{"no items", "order-1", "buyer-1", "cart-1", func([]Item) []Item { return nil }, "items"},
		{"missing product", "order-1", "buyer-1", "cart-1", func(i []Item) []Item { i[0].ProductID = ""; return i }, "productId"},
		{"missing seller", "order-1", "buyer-1", "cart-1", func(i []Item) []Item { i[0].SellerID = ""; return i }, "sellerId"},
		{"missing name", "order-1", "buyer-1", "cart-1", func(i []Item) []Item { i[0].ProductName = "  "; return i }, "productName"},
		{"zero quantity", "order-1", "buyer-1", "cart-1", func(i []Item) []Item { i[0].Quantity = 0; return i }, "quantity"},
		{"too many", "order-1", "buyer-1", "cart-1", func(i []Item) []Item { i[0].Quantity = 100; return i }, "quantity"},
		{"free item", "order-1", "buyer-1", "cart-1", func(i []Item) []Item { i[0].UnitPrice = brl(0); return i }, "unitPrice"},
		{"bad currency", "order-1", "buyer-1", "cart-1", func(i []Item) []Item { i[0].UnitPrice.Currency = "R$"; return i }, "currency"},
		{"mixed currencies", "order-1", "buyer-1", "cart-1", func(i []Item) []Item {
			i[1].UnitPrice = Money{Cents: 100, Currency: "USD"}
			return i
		}, "currency"},
		{"duplicated product", "order-1", "buyer-1", "cart-1", func(i []Item) []Item { i[1].ProductID = i[0].ProductID; return i }, "items"},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			items := validItems()
			if tt.mutate != nil {
				items = tt.mutate(items)
			}

			_, err := New(tt.id, tt.buyer, tt.cart, items, now)

			if err == nil {
				t.Fatal("expected an error")
			}
			if got := fieldOf(t, err); got != tt.field {
				t.Errorf("field = %q, want %q", got, tt.field)
			}
		})
	}
}

func TestConfirmMovesAPendingOrderToConfirmed(t *testing.T) {
	o := newValidOrder(t)
	later := now.Add(time.Minute)

	if err := o.Confirm(later); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if o.Status != StatusConfirmed || !o.UpdatedAt.Equal(later) {
		t.Errorf("status = %q, updatedAt = %v", o.Status, o.UpdatedAt)
	}
}

func TestRejectRecordsTheReason(t *testing.T) {
	o := newValidOrder(t)

	if err := o.Reject("Furadeira sem estoque", now.Add(time.Minute)); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if o.Status != StatusRejected || o.RejectionReason != "Furadeira sem estoque" {
		t.Errorf("status = %q, reason = %q", o.Status, o.RejectionReason)
	}
}

func TestRejectRequiresAReason(t *testing.T) {
	o := newValidOrder(t)

	if got := fieldOf(t, o.Reject(" ", now)); got != "reason" {
		t.Errorf("field = %q, want reason", got)
	}
	if o.Status != StatusPending {
		t.Errorf("status changed to %q", o.Status)
	}
}

func TestRepeatedTransitionsAreIdempotent(t *testing.T) {
	confirmed := newValidOrder(t)
	_ = confirmed.Confirm(now.Add(time.Minute))
	rejected := newValidOrder(t)
	_ = rejected.Reject("sem estoque", now.Add(time.Minute))

	if err := confirmed.Confirm(now.Add(time.Hour)); err != nil {
		t.Errorf("confirming twice: %v", err)
	}
	if err := rejected.Reject("outro motivo", now.Add(time.Hour)); err != nil {
		t.Errorf("rejecting twice: %v", err)
	}
	if !confirmed.UpdatedAt.Equal(now.Add(time.Minute)) {
		t.Errorf("repeated confirm changed updatedAt")
	}
	if rejected.RejectionReason != "sem estoque" {
		t.Errorf("repeated reject changed the reason to %q", rejected.RejectionReason)
	}
}

func TestConflictingTransitionsAreRejected(t *testing.T) {
	confirmed := newValidOrder(t)
	_ = confirmed.Confirm(now)
	rejected := newValidOrder(t)
	_ = rejected.Reject("sem estoque", now)

	if err := confirmed.Reject("tarde demais", now); !errors.Is(err, ErrInvalidTransition) {
		t.Errorf("reject after confirm: err = %v, want ErrInvalidTransition", err)
	}
	if err := rejected.Confirm(now); !errors.Is(err, ErrInvalidTransition) {
		t.Errorf("confirm after reject: err = %v, want ErrInvalidTransition", err)
	}
	if confirmed.Status != StatusConfirmed || rejected.Status != StatusRejected {
		t.Errorf("statuses changed: %q / %q", confirmed.Status, rejected.Status)
	}
}
