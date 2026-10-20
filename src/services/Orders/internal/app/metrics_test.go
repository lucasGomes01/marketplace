package app

import (
	"context"
	"testing"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"
)

// Métricas de negócio que o painel do Grafana usa: pedidos por status e tempo até o pedido ser resolvido.
func TestOrderMetrics(t *testing.T) {
	reader := sdkmetric.NewManualReader()
	previous := otel.GetMeterProvider()
	otel.SetMeterProvider(sdkmetric.NewMeterProvider(sdkmetric.WithReader(reader)))
	t.Cleanup(func() { otel.SetMeterProvider(previous) })

	clock := now
	svc := NewService(newMemoryRepo(), func() time.Time { return clock })
	ctx := context.Background()
	_, _ = svc.PlaceOrder(ctx, checkedOut("order-1"))
	_, _ = svc.PlaceOrder(ctx, checkedOut("order-1")) // repetido: não conta de novo
	_, _ = svc.PlaceOrder(ctx, checkedOut("order-2"))
	clock = now.Add(1500 * time.Millisecond)
	_ = svc.ConfirmOrder(ctx, StockReserved{OrderID: "order-1"})
	_ = svc.ConfirmOrder(ctx, StockReserved{OrderID: "order-1"}) // repetido: não conta de novo
	_ = svc.RejectOrder(ctx, StockRejected{OrderID: "order-2", Reason: "sem estoque"})

	var data metricdata.ResourceMetrics
	if err := reader.Collect(ctx, &data); err != nil {
		t.Fatal(err)
	}

	counts := map[string]int64{}
	var settled []metricdata.HistogramDataPoint[float64]
	for _, scope := range data.ScopeMetrics {
		for _, m := range scope.Metrics {
			switch m.Name {
			case "marketplace.orders":
				for _, p := range m.Data.(metricdata.Sum[int64]).DataPoints {
					status, _ := p.Attributes.Value(attribute.Key("status"))
					counts[status.AsString()] = p.Value
				}
			case "marketplace.order.settle.duration":
				settled = m.Data.(metricdata.Histogram[float64]).DataPoints
			}
		}
	}

	if counts["placed"] != 2 || counts["confirmed"] != 1 || counts["rejected"] != 1 {
		t.Errorf("orders by status = %v, want placed 2, confirmed 1, rejected 1", counts)
	}
	var total uint64
	var sum float64
	for _, p := range settled {
		total += p.Count
		sum += p.Sum
	}
	if total != 2 || sum != 3 {
		t.Errorf("settle duration: count %d sum %v, want 2 orders of 1.5s", total, sum)
	}
}
