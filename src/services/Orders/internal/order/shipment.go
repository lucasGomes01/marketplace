package order

import (
	"errors"
	"fmt"
	"slices"
	"time"
)

// ShipmentStatus é a etapa do envio de um vendedor, sempre nesta ordem (sem pular nem voltar).
type ShipmentStatus string

const (
	ShipmentAwaitingPicking ShipmentStatus = "AwaitingPicking"
	ShipmentPicked          ShipmentStatus = "Picked"
	ShipmentPacked          ShipmentStatus = "Packed"
	ShipmentShipped         ShipmentStatus = "Shipped"
	ShipmentDelivered       ShipmentStatus = "Delivered"
)

var shipmentSteps = []ShipmentStatus{
	ShipmentAwaitingPicking, ShipmentPicked, ShipmentPacked, ShipmentShipped, ShipmentDelivered,
}

// ErrShipmentNotFound: o vendedor não tem itens neste pedido.
var ErrShipmentNotFound = errors.New("order: shipment not found")

// Shipment é o envio dos itens de um vendedor. Datas zeradas = etapa ainda não aconteceu.
type Shipment struct {
	SellerID    string
	Status      ShipmentStatus
	PickedAt    time.Time
	PackedAt    time.Time
	ShippedAt   time.Time
	DeliveredAt time.Time
}

// newShipments cria um envio por vendedor, na ordem em que ele aparece nos itens.
func newShipments(items []Item) []Shipment {
	var shipments []Shipment
	seen := map[string]bool{}
	for _, item := range items {
		if !seen[item.SellerID] {
			seen[item.SellerID] = true
			shipments = append(shipments, Shipment{SellerID: item.SellerID, Status: ShipmentAwaitingPicking})
		}
	}
	return shipments
}

func (s *Shipment) moveTo(to ShipmentStatus, now time.Time) {
	s.Status = to
	switch to {
	case ShipmentPicked:
		s.PickedAt = now
	case ShipmentPacked:
		s.PackedAt = now
	case ShipmentShipped:
		s.ShippedAt = now
	case ShipmentDelivered:
		s.DeliveredAt = now
	}
}

// Shipment devolve uma cópia do envio do vendedor.
func (o *Order) Shipment(sellerID string) (Shipment, bool) {
	if s := o.shipment(sellerID); s != nil {
		return *s, true
	}
	return Shipment{}, false
}

func (o *Order) shipment(sellerID string) *Shipment {
	for i := range o.Shipments {
		if o.Shipments[i].SellerID == sellerID {
			return &o.Shipments[i]
		}
	}
	return nil
}

// AdvanceShipment é o vendedor levando o envio para a próxima etapa, até Shipped (Delivered é do comprador).
// Pedir a etapa atual é aceito sem mudar nada (requisição repetida).
func (o *Order) AdvanceShipment(sellerID string, to ShipmentStatus, now time.Time) error {
	if !slices.Contains(shipmentSteps, to) {
		return invalid("status", "%q is not a shipment status", to)
	}
	if to == ShipmentDelivered {
		return fmt.Errorf("%w: only the buyer confirms delivery", ErrInvalidTransition)
	}
	return o.moveShipment(sellerID, to, now)
}

// ConfirmDelivery é o comprador dizendo que recebeu o envio. Repetir é aceito.
func (o *Order) ConfirmDelivery(sellerID string, now time.Time) error {
	return o.moveShipment(sellerID, ShipmentDelivered, now)
}

func (o *Order) moveShipment(sellerID string, to ShipmentStatus, now time.Time) error {
	if o.Status != StatusConfirmed {
		return fmt.Errorf("%w: a %s order is not shipped", ErrInvalidTransition, o.Status)
	}
	s := o.shipment(sellerID)
	if s == nil {
		return ErrShipmentNotFound
	}
	current := slices.Index(shipmentSteps, s.Status)
	switch target := slices.Index(shipmentSteps, to); {
	case target == current:
		return nil
	case target != current+1:
		return fmt.Errorf("%w: cannot go from %s to %s", ErrInvalidTransition, s.Status, to)
	}
	s.moveTo(to, now)
	o.UpdatedAt = now
	return nil
}
