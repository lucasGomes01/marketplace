package app

import (
	"context"
	"fmt"

	"marketplace/orders/internal/order"
)

// AdvanceShipment é o vendedor levando o envio dele para a próxima etapa.
// Pedido inexistente ou sem envio do vendedor → ErrNotFound (não revela pedidos alheios).
func (s *Service) AdvanceShipment(ctx context.Context, sellerID, orderID string, to order.ShipmentStatus) (*order.Order, error) {
	o, err := s.repo.Get(ctx, orderID)
	if err != nil {
		return nil, err
	}
	return s.moveShipment(ctx, o, sellerID, func() error { return o.AdvanceShipment(sellerID, to, s.clock()) })
}

// ConfirmDelivery é o comprador dizendo que recebeu o envio do vendedor.
func (s *Service) ConfirmDelivery(ctx context.Context, buyerID, orderID, sellerID string) (*order.Order, error) {
	o, err := s.OrderOf(ctx, buyerID, orderID)
	if err != nil {
		return nil, err
	}
	return s.moveShipment(ctx, o, sellerID, func() error { return o.ConfirmDelivery(sellerID, s.clock()) })
}

// moveShipment aplica a regra, grava só se mudou e publica sempre: repetir a requisição republica o mesmo evento,
// o que corrige uma publicação que falhou depois de gravar (não há outbox, ver I-01).
func (s *Service) moveShipment(ctx context.Context, o *order.Order, sellerID string, apply func() error) (*order.Order, error) {
	before, ok := o.Shipment(sellerID)
	if !ok {
		return nil, fmt.Errorf("%w: no shipment of %s in order %s", ErrNotFound, sellerID, o.ID)
	}
	if err := apply(); err != nil {
		return nil, err
	}
	after, _ := o.Shipment(sellerID)
	if after.Status != before.Status {
		if err := s.repo.UpdateShipment(ctx, o, sellerID, before.Status); err != nil {
			return nil, err
		}
	}
	if after.Status == order.ShipmentAwaitingPicking {
		return o, nil // nada aconteceu ainda: não há o que anunciar
	}
	event := ShipmentStatusChanged{
		OrderID: o.ID, BuyerID: o.BuyerID, SellerID: sellerID, Status: after.Status, ChangedAt: after.ReachedAt(after.Status),
	}
	eventID := o.ID + ":" + sellerID + ":" + string(after.Status)
	if err := s.pub.Publish(ctx, EventShipmentStatusChanged, o.ID, eventID, event); err != nil {
		return nil, fmt.Errorf("publish %s: %w", EventShipmentStatusChanged, err)
	}
	return o, nil
}

// ShipmentsOf lista os pedidos confirmados com envio do vendedor, mais recentes primeiro.
func (s *Service) ShipmentsOf(ctx context.Context, sellerID string) ([]*order.Order, error) {
	return s.repo.ListBySeller(ctx, sellerID, MaxOrdersListed)
}
