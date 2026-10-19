package api

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"marketplace/orders/internal/app"
	"marketplace/orders/internal/order"
)

// twoSellerOrder: itens da s-ana e da s-bia, comprador buyer-1, já confirmado.
func twoSellerOrder(t *testing.T, id string) *order.Order {
	t.Helper()
	o, err := order.New(id, "buyer-1", "cart-1", []order.Item{
		{ProductID: "p-drill", SellerID: "s-ana", ProductName: "Furadeira", UnitPrice: order.Money{Cents: 19990, Currency: "BRL"}, Quantity: 2},
		{ProductID: "p-wrench", SellerID: "s-bia", ProductName: "Chaves", UnitPrice: order.Money{Cents: 8950, Currency: "BRL"}, Quantity: 1},
	}, now)
	if err != nil {
		t.Fatal(err)
	}
	if err := o.Confirm(now); err != nil {
		t.Fatal(err)
	}
	return o
}

type request struct {
	method, path, userID, body string
	roles                      []string
}

func send(t *testing.T, router http.Handler, r request) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequest(r.method, r.path, strings.NewReader(r.body))
	if r.userID != "" {
		req.Header.Set("Authorization", "Bearer "+token(t, r.userID, r.roles...))
	}
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	return rec
}

var seller = []string{"Buyer", "Seller"}

func shipmentsRouter(t *testing.T, failWith error) (http.Handler, *order.Order) {
	t.Helper()
	confirmed := twoSellerOrder(t, "order-1")
	pending := sampleOrder(t, "order-pending", "buyer-1")
	queries := fakeQueries{orders: []*order.Order{confirmed, pending}, failWith: failWith}
	return NewRouter(queries, authCfg, func() time.Time { return now }), confirmed
}

func problemCode(t *testing.T, rec *httptest.ResponseRecorder) string {
	t.Helper()
	var body struct {
		Code string `json:"code"`
	}
	_ = json.Unmarshal(rec.Body.Bytes(), &body)
	return body.Code
}

func TestShipmentRoutesRequireLoginAndTheSellerRole(t *testing.T) {
	router, _ := shipmentsRouter(t, nil)

	for _, r := range []request{
		{method: http.MethodGet, path: "/shipments"},
		{method: http.MethodPut, path: "/shipments/order-1/status", body: `{"status":"Picked"}`},
	} {
		if rec := send(t, router, r); rec.Code != http.StatusUnauthorized {
			t.Errorf("%s %s without token = %d, want 401", r.method, r.path, rec.Code)
		}
		r.userID = "s-ana" // só Buyer
		if rec := send(t, router, r); rec.Code != http.StatusForbidden {
			t.Errorf("%s %s as buyer = %d, want 403", r.method, r.path, rec.Code)
		}
	}
}

func TestSellerListsTheirShipmentsWithOnlyTheirItems(t *testing.T) {
	router, _ := shipmentsRouter(t, nil)

	rec := send(t, router, request{method: http.MethodGet, path: "/shipments", userID: "s-ana", roles: seller})

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d: %s", rec.Code, rec.Body.String())
	}
	var body []struct {
		OrderID   string  `json:"orderId"`
		SellerID  string  `json:"sellerId"`
		Status    string  `json:"status"`
		PickedAt  *string `json:"pickedAt"`
		ItemCount int     `json:"itemCount"`
		Total     float64 `json:"total"`
		Currency  string  `json:"currency"`
		OrderedAt string  `json:"orderedAt"`
		Items     []struct {
			ProductName string `json:"productName"`
		} `json:"items"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("invalid JSON: %v", err)
	}
	if len(body) != 1 {
		t.Fatalf("body = %s", rec.Body.String())
	}
	got := body[0]
	if got.OrderID != "order-1" || got.SellerID != "s-ana" || got.Status != "AwaitingPicking" || got.PickedAt != nil {
		t.Errorf("shipment = %+v", got)
	}
	if len(got.Items) != 1 || got.Items[0].ProductName != "Furadeira" || got.ItemCount != 2 || got.Total != 399.80 || got.Currency != "BRL" {
		t.Errorf("seller's part = %+v (other sellers' items must not show)", got)
	}
	if got.OrderedAt != "2026-10-13T12:00:00Z" {
		t.Errorf("orderedAt = %q", got.OrderedAt)
	}
}

func TestSellerAdvancesTheShipment(t *testing.T) {
	router, _ := shipmentsRouter(t, nil)

	rec := send(t, router, request{method: http.MethodPut, path: "/shipments/order-1/status", userID: "s-ana", roles: seller, body: `{"status":"Picked"}`})

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d: %s", rec.Code, rec.Body.String())
	}
	var body struct {
		Status   string `json:"status"`
		PickedAt string `json:"pickedAt"`
	}
	_ = json.Unmarshal(rec.Body.Bytes(), &body)
	if body.Status != "Picked" || body.PickedAt != "2026-10-13T12:00:00Z" {
		t.Errorf("body = %s", rec.Body.String())
	}
}

func TestShipmentErrorsBecomeProblems(t *testing.T) {
	cases := []struct {
		name     string
		r        request
		failWith error
		status   int
		code     string
	}{
		{"unknown status", request{path: "/shipments/order-1/status", userID: "s-ana", body: `{"status":"Lost"}`}, nil, 400, "shipment.invalid_status"},
		{"invalid JSON", request{path: "/shipments/order-1/status", userID: "s-ana", body: `{`}, nil, 400, "shipment.invalid_status"},
		{"unknown order", request{path: "/shipments/nope/status", userID: "s-ana", body: `{"status":"Picked"}`}, nil, 404, "shipment.not_found"},
		{"no items of this seller", request{path: "/shipments/order-1/status", userID: "s-carla", body: `{"status":"Picked"}`}, nil, 404, "shipment.not_found"},
		{"skipping a step", request{path: "/shipments/order-1/status", userID: "s-ana", body: `{"status":"Shipped"}`}, nil, 409, "shipment.invalid_transition"},
		{"seller marks delivered", request{path: "/shipments/order-1/status", userID: "s-ana", body: `{"status":"Delivered"}`}, nil, 409, "shipment.invalid_transition"},
		{"order not confirmed", request{path: "/shipments/order-pending/status", userID: "s-ana", body: `{"status":"Picked"}`}, nil, 409, "shipment.invalid_transition"},
		{"concurrent change", request{path: "/shipments/order-1/status", userID: "s-ana", body: `{"status":"Picked"}`}, app.ErrConflict, 409, "shipment.conflict"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			router, _ := shipmentsRouter(t, tc.failWith)
			tc.r.method, tc.r.roles = http.MethodPut, seller

			rec := send(t, router, tc.r)

			if rec.Code != tc.status || problemCode(t, rec) != tc.code {
				t.Errorf("got %d %q, want %d %q: %s", rec.Code, problemCode(t, rec), tc.status, tc.code, rec.Body.String())
			}
		})
	}
}

func TestBuyerConfirmsDelivery(t *testing.T) {
	router, o := shipmentsRouter(t, nil)
	for _, step := range []order.ShipmentStatus{order.ShipmentPicked, order.ShipmentPacked, order.ShipmentShipped} {
		_ = o.AdvanceShipment("s-ana", step, now)
	}
	path := "/orders/order-1/shipments/s-ana/delivery"

	if rec := send(t, router, request{method: http.MethodPost, path: path}); rec.Code != http.StatusUnauthorized {
		t.Errorf("without token = %d, want 401", rec.Code)
	}
	if rec := send(t, router, request{method: http.MethodPost, path: path, userID: "buyer-2"}); rec.Code != http.StatusNotFound {
		t.Errorf("another buyer = %d, want 404", rec.Code)
	}
	notShipped := send(t, router, request{method: http.MethodPost, path: "/orders/order-1/shipments/s-bia/delivery", userID: "buyer-1"})
	if notShipped.Code != http.StatusConflict || problemCode(t, notShipped) != "shipment.invalid_transition" {
		t.Errorf("not shipped yet = %d %s", notShipped.Code, notShipped.Body.String())
	}

	rec := send(t, router, request{method: http.MethodPost, path: path, userID: "buyer-1"})

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d: %s", rec.Code, rec.Body.String())
	}
	var body struct {
		ID        string `json:"id"`
		Shipments []struct {
			SellerID    string  `json:"sellerId"`
			Status      string  `json:"status"`
			DeliveredAt *string `json:"deliveredAt"`
		} `json:"shipments"`
	}
	_ = json.Unmarshal(rec.Body.Bytes(), &body)
	if body.ID != "order-1" || len(body.Shipments) != 2 || body.Shipments[0].Status != "Delivered" || body.Shipments[0].DeliveredAt == nil {
		t.Errorf("body = %s", rec.Body.String())
	}
	if body.Shipments[1].SellerID != "s-bia" || body.Shipments[1].Status != "AwaitingPicking" || body.Shipments[1].DeliveredAt != nil {
		t.Errorf("s-bia = %+v", body.Shipments[1])
	}
}
