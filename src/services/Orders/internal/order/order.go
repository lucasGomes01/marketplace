// Package order contém o domínio do pedido: regras e ciclo de status, sem banco, HTTP ou Kafka.
package order

import (
	"errors"
	"fmt"
	"strings"
	"time"
)

// MaxQuantityPerItem espelha a regra do carrinho.
const MaxQuantityPerItem = 99

// Status do pedido: nasce Pending e termina Confirmed ou Rejected.
type Status string

const (
	StatusPending   Status = "Pending"
	StatusConfirmed Status = "Confirmed"
	StatusRejected  Status = "Rejected"
)

// ErrInvalidTransition indica uma mudança de status proibida (ex.: confirmar um pedido recusado).
var ErrInvalidTransition = errors.New("order: invalid status transition")

// DomainError é a violação de uma regra, com o campo envolvido (mesmo padrão dos serviços .NET).
type DomainError struct {
	Field   string
	Message string
}

func (e *DomainError) Error() string { return fmt.Sprintf("%s: %s", e.Field, e.Message) }

func invalid(field, format string, args ...any) error {
	return &DomainError{Field: field, Message: fmt.Sprintf(format, args...)}
}

// Money em centavos: nunca float para dinheiro.
type Money struct {
	Cents    int64
	Currency string
}

// Item é o snapshot do produto no momento do checkout.
type Item struct {
	ProductID   string
	SellerID    string
	ProductName string
	UnitPrice   Money
	Quantity    int
}

// Subtotal é preço × quantidade.
func (i Item) Subtotal() Money {
	return Money{Cents: i.UnitPrice.Cents * int64(i.Quantity), Currency: i.UnitPrice.Currency}
}

// Order é o pedido de um comprador, criado a partir do checkout de um carrinho.
type Order struct {
	ID              string
	BuyerID         string
	CartID          string
	Items           []Item
	Shipments       []Shipment
	Status          Status
	RejectionReason string
	CreatedAt       time.Time
	UpdatedAt       time.Time
}

// New valida e cria um pedido Pending. Os itens são copiados (o chamador pode reutilizar o slice).
func New(id, buyerID, cartID string, items []Item, now time.Time) (*Order, error) {
	if strings.TrimSpace(id) == "" {
		return nil, invalid("id", "order id is required")
	}
	if strings.TrimSpace(buyerID) == "" {
		return nil, invalid("buyerId", "buyer is required")
	}
	if strings.TrimSpace(cartID) == "" {
		return nil, invalid("cartId", "cart is required")
	}
	if len(items) == 0 {
		return nil, invalid("items", "an order needs at least one item")
	}

	currency := items[0].UnitPrice.Currency
	seen := make(map[string]bool, len(items))
	for _, item := range items {
		if err := validateItem(item, currency); err != nil {
			return nil, err
		}
		if seen[item.ProductID] {
			return nil, invalid("items", "product %s appears more than once", item.ProductID)
		}
		seen[item.ProductID] = true
	}

	return &Order{
		ID:        id,
		BuyerID:   buyerID,
		CartID:    cartID,
		Items:     append([]Item(nil), items...),
		Shipments: newShipments(items),
		Status:    StatusPending,
		CreatedAt: now,
		UpdatedAt: now,
	}, nil
}

func validateItem(item Item, currency string) error {
	switch {
	case strings.TrimSpace(item.ProductID) == "":
		return invalid("productId", "product is required")
	case strings.TrimSpace(item.SellerID) == "":
		return invalid("sellerId", "seller is required")
	case strings.TrimSpace(item.ProductName) == "":
		return invalid("productName", "product name is required")
	case item.Quantity < 1 || item.Quantity > MaxQuantityPerItem:
		return invalid("quantity", "quantity must be between 1 and %d", MaxQuantityPerItem)
	case item.UnitPrice.Cents <= 0:
		return invalid("unitPrice", "unit price must be greater than zero")
	case !isCurrencyCode(item.UnitPrice.Currency):
		return invalid("currency", "%q is not a valid currency code", item.UnitPrice.Currency)
	case item.UnitPrice.Currency != currency:
		return invalid("currency", "an order has a single currency (%s), got %s", currency, item.UnitPrice.Currency)
	}
	return nil
}

func isCurrencyCode(code string) bool {
	if len(code) != 3 {
		return false
	}
	for _, r := range code {
		if r < 'A' || r > 'Z' {
			return false
		}
	}
	return true
}

// Total soma os subtotais.
func (o *Order) Total() Money {
	total := Money{Currency: o.Items[0].UnitPrice.Currency}
	for _, item := range o.Items {
		total.Cents += item.Subtotal().Cents
	}
	return total
}

// ItemCount soma as quantidades.
func (o *Order) ItemCount() int {
	count := 0
	for _, item := range o.Items {
		count += item.Quantity
	}
	return count
}

// Confirm marca o pedido como confirmado. Repetir é aceito (eventos podem chegar duplicados).
func (o *Order) Confirm(now time.Time) error {
	switch o.Status {
	case StatusConfirmed:
		return nil
	case StatusPending:
		o.Status = StatusConfirmed
		o.UpdatedAt = now
		return nil
	default:
		return fmt.Errorf("%w: cannot confirm a %s order", ErrInvalidTransition, o.Status)
	}
}

// Reject recusa o pedido com um motivo. Repetir é aceito e mantém o motivo original.
func (o *Order) Reject(reason string, now time.Time) error {
	reason = strings.TrimSpace(reason)
	switch {
	case o.Status == StatusRejected:
		return nil
	case o.Status != StatusPending:
		return fmt.Errorf("%w: cannot reject a %s order", ErrInvalidTransition, o.Status)
	case reason == "":
		return invalid("reason", "a rejection needs a reason")
	}
	o.Status = StatusRejected
	o.RejectionReason = reason
	o.UpdatedAt = now
	return nil
}
