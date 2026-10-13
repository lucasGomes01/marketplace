// Package auth valida o JWT emitido pelo serviço Users (BuildingBlocks.Auth): HS256, mesmo issuer,
// audience e chave. Só stdlib — sem dependência de biblioteca JWT.
package auth

import (
	"context"
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"slices"
	"strings"
	"time"
)

// ClockSkew tolera pequenas diferenças de relógio entre serviços (igual ao .NET).
const ClockSkew = 30 * time.Second

// ErrInvalidToken cobre qualquer token que não deve ser aceito.
var ErrInvalidToken = errors.New("auth: invalid token")

// Config é a mesma da seção Jwt dos serviços .NET.
type Config struct {
	Issuer     string
	Audience   string
	SigningKey []byte
}

// Claims usadas pelo Orders.
type Claims struct {
	UserID string
	Email  string
	Roles  []string
}

// HasRole diz se o token tem o papel.
func (c Claims) HasRole(role string) bool { return slices.Contains(c.Roles, role) }

// stringOrList aceita "x" ou ["x","y"] (o .NET emite lista quando há mais de um papel).
type stringOrList []string

func (s *stringOrList) UnmarshalJSON(data []byte) error {
	var one string
	if err := json.Unmarshal(data, &one); err == nil {
		*s = []string{one}
		return nil
	}
	var many []string
	if err := json.Unmarshal(data, &many); err != nil {
		return err
	}
	*s = many
	return nil
}

type header struct {
	Alg string `json:"alg"`
}

type payload struct {
	Iss   string       `json:"iss"`
	Aud   stringOrList `json:"aud"`
	Sub   string       `json:"sub"`
	Email string       `json:"email"`
	Role  stringOrList `json:"role"`
	Exp   *int64       `json:"exp"`
	Nbf   *int64       `json:"nbf"`
}

func invalid(reason string) error { return fmt.Errorf("%w: %s", ErrInvalidToken, reason) }

// Validate confere assinatura, algoritmo, issuer, audience e validade.
func (c Config) Validate(token string, now time.Time) (Claims, error) {
	parts := strings.Split(token, ".")
	if len(parts) != 3 {
		return Claims{}, invalid("malformed")
	}

	var h header
	if err := decodeSegment(parts[0], &h); err != nil || h.Alg != "HS256" {
		return Claims{}, invalid("unsupported algorithm")
	}

	signature, err := base64.RawURLEncoding.DecodeString(parts[2])
	if err != nil {
		return Claims{}, invalid("bad signature encoding")
	}
	mac := hmac.New(sha256.New, c.SigningKey)
	mac.Write([]byte(parts[0] + "." + parts[1]))
	if !hmac.Equal(signature, mac.Sum(nil)) {
		return Claims{}, invalid("bad signature")
	}

	var p payload
	if err := decodeSegment(parts[1], &p); err != nil {
		return Claims{}, invalid("bad payload")
	}
	switch {
	case p.Iss != c.Issuer:
		return Claims{}, invalid("wrong issuer")
	case !slices.Contains(p.Aud, c.Audience):
		return Claims{}, invalid("wrong audience")
	case p.Exp == nil || now.After(time.Unix(*p.Exp, 0).Add(ClockSkew)):
		return Claims{}, invalid("expired")
	case p.Nbf != nil && now.Before(time.Unix(*p.Nbf, 0).Add(-ClockSkew)):
		return Claims{}, invalid("not yet valid")
	case p.Sub == "":
		return Claims{}, invalid("missing subject")
	}

	return Claims{UserID: p.Sub, Email: p.Email, Roles: p.Role}, nil
}

func decodeSegment(segment string, v any) error {
	raw, err := base64.RawURLEncoding.DecodeString(segment)
	if err != nil {
		return err
	}
	return json.Unmarshal(raw, v)
}

type contextKey struct{}

// FromContext devolve as claims colocadas pelo middleware Require.
func FromContext(ctx context.Context) (Claims, bool) {
	claims, ok := ctx.Value(contextKey{}).(Claims)
	return claims, ok
}

// Require exige "Authorization: Bearer <jwt>" válido; senão responde 401.
func Require(cfg Config, clock func() time.Time) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			token, found := strings.CutPrefix(r.Header.Get("Authorization"), "Bearer ")
			if !found {
				w.Header().Set("WWW-Authenticate", "Bearer")
				w.WriteHeader(http.StatusUnauthorized)
				return
			}
			claims, err := cfg.Validate(token, clock())
			if err != nil {
				w.Header().Set("WWW-Authenticate", `Bearer error="invalid_token"`)
				w.WriteHeader(http.StatusUnauthorized)
				return
			}
			next.ServeHTTP(w, r.WithContext(context.WithValue(r.Context(), contextKey{}, claims)))
		})
	}
}
