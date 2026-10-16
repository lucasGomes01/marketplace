package app

import (
	"context"
	"strings"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"

	"marketplace/orders/internal/order"
)

// metrics são as métricas de negócio do Orders (OpenTelemetry; no-op sem telemetria ligada).
type metrics struct {
	orders  metric.Int64Counter
	settled metric.Float64Histogram
}

func newMetrics() metrics {
	meter := otel.Meter("marketplace/orders")
	orders, _ := meter.Int64Counter("marketplace.orders",
		metric.WithDescription("Pedidos por status: placed (criado), confirmed, rejected."))
	settled, _ := meter.Float64Histogram("marketplace.order.settle.duration",
		metric.WithUnit("s"),
		metric.WithDescription("Do pedido criado até confirmado ou recusado (passa pelo Kafka e pelo Catalog)."),
		metric.WithExplicitBucketBoundaries(0.1, 0.25, 0.5, 1, 2, 5, 10, 30, 60))
	return metrics{orders: orders, settled: settled}
}

func (m metrics) placed(ctx context.Context) {
	m.orders.Add(ctx, 1, metric.WithAttributes(attribute.String("status", "placed")))
}

func (m metrics) resolved(ctx context.Context, o *order.Order, now time.Time) {
	status := attribute.String("status", strings.ToLower(string(o.Status)))
	m.orders.Add(ctx, 1, metric.WithAttributes(status))
	m.settled.Record(ctx, now.Sub(o.CreatedAt).Seconds(), metric.WithAttributes(status))
}
