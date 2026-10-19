package api

import (
	"encoding/json"
	"net/http"
	"time"

	"marketplace/orders/internal/auth"
	"marketplace/orders/internal/order"
)

// shipmentResponse é o envio de um vendedor; datas null enquanto a etapa não aconteceu.
type shipmentResponse struct {
	SellerID    string               `json:"sellerId"`
	Status      order.ShipmentStatus `json:"status"`
	PickedAt    *time.Time           `json:"pickedAt"`
	PackedAt    *time.Time           `json:"packedAt"`
	ShippedAt   *time.Time           `json:"shippedAt"`
	DeliveredAt *time.Time           `json:"deliveredAt"`
}

func toShipmentResponse(s order.Shipment) shipmentResponse {
	return shipmentResponse{
		SellerID: s.SellerID, Status: s.Status,
		PickedAt: optional(s.PickedAt), PackedAt: optional(s.PackedAt),
		ShippedAt: optional(s.ShippedAt), DeliveredAt: optional(s.DeliveredAt),
	}
}

func optional(t time.Time) *time.Time {
	if t.IsZero() {
		return nil
	}
	utc := t.UTC()
	return &utc
}

// sellerShipmentResponse é o que o vendedor vê: o envio dele com só os itens dele.
type sellerShipmentResponse struct {
	OrderID string `json:"orderId"`
	shipmentResponse
	Items     []itemResponse `json:"items"`
	ItemCount int            `json:"itemCount"`
	Total     money          `json:"total"`
	Currency  string         `json:"currency"`
	OrderedAt time.Time      `json:"orderedAt"`
}

func toSellerShipment(o *order.Order, sellerID string) sellerShipmentResponse {
	shipment, _ := o.Shipment(sellerID)
	body := sellerShipmentResponse{
		OrderID: o.ID, shipmentResponse: toShipmentResponse(shipment),
		Items: []itemResponse{}, Currency: o.Total().Currency, OrderedAt: o.CreatedAt.UTC(),
	}
	for _, item := range o.Items {
		if item.SellerID == sellerID {
			body.Items = append(body.Items, toItemResponse(item))
			body.ItemCount += item.Quantity
			body.Total += money(item.Subtotal().Cents)
		}
	}
	return body
}

func (h handlers) listShipments(w http.ResponseWriter, r *http.Request) {
	claims, _ := auth.FromContext(r.Context())
	orders, err := h.queries.ShipmentsOf(r.Context(), claims.UserID)
	if err != nil {
		serverError(w, r, err)
		return
	}
	body := make([]sellerShipmentResponse, len(orders))
	for i, o := range orders {
		body[i] = toSellerShipment(o, claims.UserID)
	}
	writeJSON(w, http.StatusOK, body)
}

func (h handlers) advanceShipment(w http.ResponseWriter, r *http.Request) {
	claims, _ := auth.FromContext(r.Context())
	var body struct {
		Status order.ShipmentStatus `json:"status"`
	}
	if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
		writeProblem(w, http.StatusBadRequest, "Body must be {\"status\": \"Picked\" | \"Packed\" | \"Shipped\"}.", "shipment.invalid_status")
		return
	}
	o, err := h.queries.AdvanceShipment(r.Context(), claims.UserID, r.PathValue("orderId"), body.Status)
	if err != nil {
		problemFor(w, r, err)
		return
	}
	writeJSON(w, http.StatusOK, toSellerShipment(o, claims.UserID))
}

func (h handlers) confirmDelivery(w http.ResponseWriter, r *http.Request) {
	claims, _ := auth.FromContext(r.Context())
	o, err := h.queries.ConfirmDelivery(r.Context(), claims.UserID, r.PathValue("id"), r.PathValue("sellerId"))
	if err != nil {
		problemFor(w, r, err)
		return
	}
	writeJSON(w, http.StatusOK, toResponse(o))
}
