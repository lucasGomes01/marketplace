package api

import (
	"context"
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"marketplace/orders/internal/app"
	"marketplace/orders/internal/auth"
	"marketplace/orders/internal/order"
)

var (
	now     = time.Date(2026, 10, 13, 12, 0, 0, 0, time.UTC)
	authCfg = auth.Config{Issuer: "marketplace-users", Audience: "marketplace", SigningKey: []byte("test-signing-key-with-at-least-32-chars!")}
)

type fakeQueries struct{ orders []*order.Order }

func (f fakeQueries) OrdersOf(_ context.Context, buyerID string) ([]*order.Order, error) {
	var mine []*order.Order
	for _, o := range f.orders {
		if o.BuyerID == buyerID {
			mine = append(mine, o)
		}
	}
	return mine, nil
}

func (f fakeQueries) OrderOf(_ context.Context, buyerID, id string) (*order.Order, error) {
	for _, o := range f.orders {
		if o.ID == id && o.BuyerID == buyerID {
			return o, nil
		}
	}
	return nil, app.ErrNotFound
}

func token(t *testing.T, userID string) string {
	t.Helper()
	enc := func(v any) string { b, _ := json.Marshal(v); return base64.RawURLEncoding.EncodeToString(b) }
	unsigned := enc(map[string]any{"alg": "HS256", "typ": "JWT"}) + "." + enc(map[string]any{
		"iss": authCfg.Issuer, "aud": authCfg.Audience, "sub": userID, "role": []string{"Buyer"}, "exp": now.Add(time.Hour).Unix(),
	})
	mac := hmac.New(sha256.New, authCfg.SigningKey)
	mac.Write([]byte(unsigned))
	return unsigned + "." + base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
}

func sampleOrder(t *testing.T, id, buyer string) *order.Order {
	t.Helper()
	o, err := order.New(id, buyer, "cart-1", []order.Item{
		{ProductID: "p-drill", SellerID: "s-ana", ProductName: "Furadeira", UnitPrice: order.Money{Cents: 19990, Currency: "BRL"}, Quantity: 2},
	}, now)
	if err != nil {
		t.Fatal(err)
	}
	return o
}

func serve(t *testing.T, router http.Handler, path, userID string) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequest(http.MethodGet, path, nil)
	if userID != "" {
		req.Header.Set("Authorization", "Bearer "+token(t, userID))
	}
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	return rec
}

func newRouter(t *testing.T) http.Handler {
	queries := fakeQueries{orders: []*order.Order{sampleOrder(t, "order-1", "buyer-1"), sampleOrder(t, "order-2", "buyer-2")}}
	return NewRouter(queries, authCfg, func() time.Time { return now })
}

func TestOrdersRequireLogin(t *testing.T) {
	router := newRouter(t)

	for _, path := range []string{"/orders", "/orders/order-1"} {
		if rec := serve(t, router, path, ""); rec.Code != http.StatusUnauthorized {
			t.Errorf("GET %s without token = %d, want 401", path, rec.Code)
		}
	}
}

func TestListReturnsOnlyTheBuyersOrdersAsJSON(t *testing.T) {
	rec := serve(t, newRouter(t), "/orders", "buyer-1")

	if rec.Code != http.StatusOK || rec.Header().Get("Content-Type") != "application/json" {
		t.Fatalf("status = %d, content-type = %q", rec.Code, rec.Header().Get("Content-Type"))
	}
	var body []map[string]any
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("invalid JSON: %v\n%s", err, rec.Body.String())
	}
	if len(body) != 1 || body[0]["id"] != "order-1" {
		t.Errorf("body = %v", body)
	}
}

func TestOrderJSONHasTheExpectedShape(t *testing.T) {
	rec := serve(t, newRouter(t), "/orders/order-1", "buyer-1")

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d", rec.Code)
	}
	var body struct {
		ID        string  `json:"id"`
		Status    string  `json:"status"`
		Total     float64 `json:"total"`
		Currency  string  `json:"currency"`
		ItemCount int     `json:"itemCount"`
		CreatedAt string  `json:"createdAt"`
		Items     []struct {
			ProductName string  `json:"productName"`
			UnitPrice   float64 `json:"unitPrice"`
			Quantity    int     `json:"quantity"`
			Subtotal    float64 `json:"subtotal"`
		} `json:"items"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &body); err != nil {
		t.Fatalf("invalid JSON: %v", err)
	}
	if body.Status != "Pending" || body.Total != 399.80 || body.Currency != "BRL" || body.ItemCount != 2 {
		t.Errorf("body = %+v", body)
	}
	if len(body.Items) != 1 || body.Items[0].UnitPrice != 199.90 || body.Items[0].Subtotal != 399.80 {
		t.Errorf("items = %+v", body.Items)
	}
	if body.CreatedAt != "2026-10-13T12:00:00Z" {
		t.Errorf("createdAt = %q", body.CreatedAt)
	}
	// Dinheiro sai como número JSON com 2 casas (igual às APIs .NET), não como string.
	if !contains(rec.Body.String(), `"total":399.80`) {
		t.Errorf("total should be the JSON number 399.80: %s", rec.Body.String())
	}
}

func TestAnotherBuyersOrderIsNotFound(t *testing.T) {
	rec := serve(t, newRouter(t), "/orders/order-2", "buyer-1")

	if rec.Code != http.StatusNotFound {
		t.Errorf("status = %d, want 404", rec.Code)
	}
}

func contains(s, sub string) bool {
	for i := 0; i+len(sub) <= len(s); i++ {
		if s[i:i+len(sub)] == sub {
			return true
		}
	}
	return false
}
