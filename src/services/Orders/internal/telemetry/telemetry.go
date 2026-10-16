// Package telemetry liga o OpenTelemetry do Orders: traces, métricas e logs via OTLP, igual aos serviços .NET
// (BuildingBlocks.Observability). Só exporta quando OTEL_EXPORTER_OTLP_ENDPOINT está definido — sem ele (testes,
// go run) os provedores globais continuam no-op e nada sai da máquina.
package telemetry

import (
	"context"
	"errors"
	"log/slog"
	"net/http"
	"os"

	"go.opentelemetry.io/contrib/bridges/otelslog"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploggrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetricgrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracegrpc"
	"go.opentelemetry.io/otel/log/global"
	"go.opentelemetry.io/otel/propagation"
	sdklog "go.opentelemetry.io/otel/sdk/log"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	semconv "go.opentelemetry.io/otel/semconv/v1.40.0"
)

// Enabled diz se há para onde exportar.
func Enabled() bool { return os.Getenv("OTEL_EXPORTER_OTLP_ENDPOINT") != "" }

// Setup registra os provedores globais e devolve a função que esvazia os buffers no desligamento.
// O propagador é sempre W3C (traceparent), o mesmo dos serviços .NET — inclusive nos headers do Kafka.
func Setup(ctx context.Context, serviceName string) (shutdown func(context.Context) error, err error) {
	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(propagation.TraceContext{}, propagation.Baggage{}))
	if !Enabled() {
		return func(context.Context) error { return nil }, nil
	}

	host, _ := os.Hostname()
	// Sem versão de convenção própria: herda a do SDK (versões diferentes dão "conflicting Schema URL").
	res, err := resource.Merge(resource.Default(), resource.NewSchemaless(
		semconv.ServiceName(serviceName),
		semconv.ServiceNamespace("marketplace"),
		semconv.ServiceInstanceID(host),
	))
	if err != nil {
		return nil, err
	}

	traceExporter, err := otlptracegrpc.New(ctx)
	if err != nil {
		return nil, err
	}
	tracer := sdktrace.NewTracerProvider(sdktrace.WithBatcher(traceExporter), sdktrace.WithResource(res))
	otel.SetTracerProvider(tracer)

	metricExporter, err := otlpmetricgrpc.New(ctx)
	if err != nil {
		return nil, err
	}
	meter := sdkmetric.NewMeterProvider(sdkmetric.WithReader(sdkmetric.NewPeriodicReader(metricExporter)), sdkmetric.WithResource(res))
	otel.SetMeterProvider(meter)

	logExporter, err := otlploggrpc.New(ctx)
	if err != nil {
		return nil, err
	}
	logger := sdklog.NewLoggerProvider(sdklog.WithProcessor(sdklog.NewBatchProcessor(logExporter)), sdklog.WithResource(res))
	global.SetLoggerProvider(logger)

	return func(ctx context.Context) error {
		return errors.Join(tracer.Shutdown(ctx), meter.Shutdown(ctx), logger.Shutdown(ctx))
	}, nil
}

// Logger escreve no stdout (JSON, como antes) e, com telemetria ligada, também no OTLP — com trace_id do contexto.
func Logger(serviceName string) *slog.Logger {
	stdout := slog.NewJSONHandler(os.Stdout, nil)
	if !Enabled() {
		return slog.New(stdout)
	}
	return slog.New(slog.NewMultiHandler(stdout, otelslog.NewHandler(serviceName)))
}

// HTTP instrumenta o servidor: um span e a métrica http.server.request.duration por requisição (como no .NET).
// /health fica de fora: é consultado o tempo todo e só poluiria os traces.
func HTTP(handler http.Handler) http.Handler {
	return otelhttp.NewHandler(handler, "orders",
		otelhttp.WithFilter(func(r *http.Request) bool { return r.URL.Path != "/health" }),
		otelhttp.WithSpanNameFormatter(func(_ string, r *http.Request) string {
			if r.Pattern != "" {
				return r.Pattern
			}
			return r.Method
		}))
}
