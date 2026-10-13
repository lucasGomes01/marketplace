package api

import (
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"net/http"
	"time"

	"marketplace/orders/internal/app"
	"marketplace/orders/internal/auth"
	"marketplace/orders/internal/order"
)

// OrderQueries é o que a API precisa da camada app.
type OrderQueries interface {
	OrdersOf(ctx context.Context, buyerID string) ([]*order.Order, error)
	OrderOf(ctx context.Context, buyerID, id string) (*order.Order, error)
}

func NewRouter(queries OrderQueries, authCfg auth.Config, clock func() time.Time) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", health)

	requireLogin := auth.Require(authCfg, clock)
	h := handlers{queries: queries}
	mux.Handle("GET /orders", requireLogin(http.HandlerFunc(h.list)))
	mux.Handle("GET /orders/{id}", requireLogin(http.HandlerFunc(h.get)))
	return mux
}

func health(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]string{"service": "orders", "status": "healthy"})
}

type handlers struct{ queries OrderQueries }

func (h handlers) list(w http.ResponseWriter, r *http.Request) {
	claims, _ := auth.FromContext(r.Context())
	orders, err := h.queries.OrdersOf(r.Context(), claims.UserID)
	if err != nil {
		serverError(w, r, err)
		return
	}
	body := make([]orderResponse, len(orders))
	for i, o := range orders {
		body[i] = toResponse(o)
	}
	writeJSON(w, http.StatusOK, body)
}

func (h handlers) get(w http.ResponseWriter, r *http.Request) {
	claims, _ := auth.FromContext(r.Context())
	o, err := h.queries.OrderOf(r.Context(), claims.UserID, r.PathValue("id"))
	switch {
	case errors.Is(err, app.ErrNotFound):
		writeProblem(w, http.StatusNotFound, "Order not found.", "order.not_found")
	case err != nil:
		serverError(w, r, err)
	default:
		writeJSON(w, http.StatusOK, toResponse(o))
	}
}

// money é escrito como número JSON com 2 casas (399.80), igual ao decimal das APIs .NET.
type money int64

func (m money) MarshalJSON() ([]byte, error) { return []byte(order.FormatCents(int64(m))), nil }

type itemResponse struct {
	ProductID   string `json:"productId"`
	SellerID    string `json:"sellerId"`
	ProductName string `json:"productName"`
	UnitPrice   money  `json:"unitPrice"`
	Currency    string `json:"currency"`
	Quantity    int    `json:"quantity"`
	Subtotal    money  `json:"subtotal"`
}

type orderResponse struct {
	ID              string         `json:"id"`
	Status          order.Status   `json:"status"`
	RejectionReason *string        `json:"rejectionReason"`
	Items           []itemResponse `json:"items"`
	ItemCount       int            `json:"itemCount"`
	Total           money          `json:"total"`
	Currency        string         `json:"currency"`
	CreatedAt       time.Time      `json:"createdAt"`
	UpdatedAt       time.Time      `json:"updatedAt"`
}

func toResponse(o *order.Order) orderResponse {
	items := make([]itemResponse, len(o.Items))
	for i, item := range o.Items {
		items[i] = itemResponse{
			ProductID: item.ProductID, SellerID: item.SellerID, ProductName: item.ProductName,
			UnitPrice: money(item.UnitPrice.Cents), Currency: item.UnitPrice.Currency,
			Quantity: item.Quantity, Subtotal: money(item.Subtotal().Cents),
		}
	}
	var reason *string
	if o.RejectionReason != "" {
		reason = &o.RejectionReason
	}
	total := o.Total()
	return orderResponse{
		ID: o.ID, Status: o.Status, RejectionReason: reason, Items: items, ItemCount: o.ItemCount(),
		Total: money(total.Cents), Currency: total.Currency, CreatedAt: o.CreatedAt.UTC(), UpdatedAt: o.UpdatedAt.UTC(),
	}
}

// writeProblem responde ProblemDetails (RFC 9457) com "code", como os serviços .NET.
func writeProblem(w http.ResponseWriter, status int, title, code string) {
	w.Header().Set("Content-Type", "application/problem+json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]any{"status": status, "title": title, "code": code})
}

func serverError(w http.ResponseWriter, r *http.Request, err error) {
	slog.ErrorContext(r.Context(), "request failed", "path", r.URL.Path, "error", err)
	writeProblem(w, http.StatusInternalServerError, "Unexpected error.", "server.error")
}

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}
