package outbox

import (
	"context"
	"sync"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/metric"
)

// Gauges registra a saúde do outbox (mesmos nomes dos serviços .NET, para o mesmo painel) e devolve o Observe do
// Relay. Só a instância com o relay ativo informa: as outras não sabem o estado da fila.
func Gauges(clock func() time.Time) func(*Stats) {
	var mu sync.Mutex
	var last *Stats
	var measuredAt time.Time

	meter := otel.Meter("marketplace/orders")
	pending, _ := meter.Int64ObservableGauge("marketplace.outbox.pending",
		metric.WithDescription("Eventos gravados no outbox que ainda não foram publicados no Kafka."))
	oldest, _ := meter.Float64ObservableGauge("marketplace.outbox.oldest_age", metric.WithUnit("s"),
		metric.WithDescription("Há quanto tempo o evento pendente mais antigo espera (0 = nada pendente)."))
	_, _ = meter.RegisterCallback(func(_ context.Context, o metric.Observer) error {
		mu.Lock()
		defer mu.Unlock()
		if last == nil {
			return nil
		}
		o.ObserveInt64(pending, int64(last.Pending))
		age := 0.0
		if !last.OldestPendingSince.IsZero() {
			age = max(0, measuredAt.Sub(last.OldestPendingSince).Seconds())
		}
		o.ObserveFloat64(oldest, age)
		return nil
	}, pending, oldest)

	return func(stats *Stats) {
		mu.Lock()
		defer mu.Unlock()
		last, measuredAt = stats, clock()
	}
}
