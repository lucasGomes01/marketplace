package auth

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"
)

var (
	now = time.Date(2026, 10, 13, 12, 0, 0, 0, time.UTC)
	cfg = Config{Issuer: "marketplace-users", Audience: "marketplace", SigningKey: []byte("test-signing-key-with-at-least-32-chars!")}
)

// sign monta um JWT como o BuildingBlocks.Auth do .NET emite.
func sign(t *testing.T, header, payload map[string]any, key []byte) string {
	t.Helper()
	enc := func(v any) string {
		b, err := json.Marshal(v)
		if err != nil {
			t.Fatal(err)
		}
		return base64.RawURLEncoding.EncodeToString(b)
	}
	unsigned := enc(header) + "." + enc(payload)
	mac := hmac.New(sha256.New, key)
	mac.Write([]byte(unsigned))
	return unsigned + "." + base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
}

func validPayload() map[string]any {
	return map[string]any{
		"iss":   "marketplace-users",
		"aud":   "marketplace",
		"sub":   "user-1",
		"email": "maria@example.com",
		"role":  []string{"Buyer", "Seller"},
		"nbf":   now.Add(-time.Minute).Unix(),
		"exp":   now.Add(time.Hour).Unix(),
	}
}

var hs256 = map[string]any{"alg": "HS256", "typ": "JWT"}

func TestValidTokenReturnsTheClaims(t *testing.T) {
	claims, err := cfg.Validate(sign(t, hs256, validPayload(), cfg.SigningKey), now)

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if claims.UserID != "user-1" || claims.Email != "maria@example.com" {
		t.Errorf("claims = %+v", claims)
	}
	if !claims.HasRole("Seller") || !claims.HasRole("Buyer") || claims.HasRole("Admin") {
		t.Errorf("roles = %v", claims.Roles)
	}
}

func TestSingleRoleAsStringIsAccepted(t *testing.T) {
	payload := validPayload()
	payload["role"] = "Buyer"

	claims, err := cfg.Validate(sign(t, hs256, payload, cfg.SigningKey), now)

	if err != nil || !claims.HasRole("Buyer") {
		t.Fatalf("claims = %+v, err = %v", claims, err)
	}
}

func TestInvalidTokensAreRejected(t *testing.T) {
	mutate := func(f func(map[string]any)) map[string]any {
		p := validPayload()
		f(p)
		return p
	}
	tests := []struct {
		name  string
		token string
	}{
		{"wrong key", sign(t, hs256, validPayload(), []byte("another-key-with-more-than-32-characters"))},
		{"expired", sign(t, hs256, mutate(func(p map[string]any) { p["exp"] = now.Add(-time.Minute).Unix() }), cfg.SigningKey)},
		{"not yet valid", sign(t, hs256, mutate(func(p map[string]any) { p["nbf"] = now.Add(time.Hour).Unix() }), cfg.SigningKey)},
		{"other issuer", sign(t, hs256, mutate(func(p map[string]any) { p["iss"] = "someone-else" }), cfg.SigningKey)},
		{"other audience", sign(t, hs256, mutate(func(p map[string]any) { p["aud"] = "other" }), cfg.SigningKey)},
		{"no subject", sign(t, hs256, mutate(func(p map[string]any) { delete(p, "sub") }), cfg.SigningKey)},
		{"alg none", sign(t, map[string]any{"alg": "none"}, validPayload(), cfg.SigningKey)},
		{"garbage", "not.a.jwt"},
		{"two parts", "abc.def"},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if _, err := cfg.Validate(tt.token, now); !errors.Is(err, ErrInvalidToken) {
				t.Errorf("err = %v, want ErrInvalidToken", err)
			}
		})
	}
}

func TestClockSkewOf30SecondsIsTolerated(t *testing.T) {
	payload := validPayload()
	payload["exp"] = now.Add(-20 * time.Second).Unix()

	if _, err := cfg.Validate(sign(t, hs256, payload, cfg.SigningKey), now); err != nil {
		t.Errorf("unexpected error: %v", err)
	}
}

func TestMiddlewareRequiresABearerToken(t *testing.T) {
	var seen *Claims
	handler := Require(cfg, func() time.Time { return now })(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		claims, _ := FromContext(r.Context())
		seen = &claims
	}))

	anonymous := httptest.NewRecorder()
	handler.ServeHTTP(anonymous, httptest.NewRequest(http.MethodGet, "/orders", nil))

	authorized := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodGet, "/orders", nil)
	req.Header.Set("Authorization", "Bearer "+sign(t, hs256, validPayload(), cfg.SigningKey))
	handler.ServeHTTP(authorized, req)

	if anonymous.Code != http.StatusUnauthorized {
		t.Errorf("anonymous status = %d, want 401", anonymous.Code)
	}
	if authorized.Code != http.StatusOK || seen == nil || seen.UserID != "user-1" {
		t.Errorf("authorized status = %d, claims = %+v", authorized.Code, seen)
	}
}
