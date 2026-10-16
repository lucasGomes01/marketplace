package telemetry

import (
	"context"
	"testing"
	"time"
)

// Com o endereço OTLP definido, Setup monta os três provedores (não precisa do coletor no ar para isso).
// Pegou um erro real: "conflicting Schema URL" entre a versão de convenções do SDK e a nossa.
func TestSetupWithEndpoint(t *testing.T) {
	t.Setenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:4317")

	shutdown, err := Setup(context.Background(), "orders-test")
	if err != nil {
		t.Fatalf("setup: %v", err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	_ = shutdown(ctx) // sem coletor, esvaziar os buffers pode falhar — o que importa é ter subido
}
