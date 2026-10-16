using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    /// <summary>Fonte dos traces e métricas próprios do marketplace (Kafka, regras de negócio).</summary>
    public const string SourceName = "Marketplace";

    /// <summary>
    /// Traces, métricas e logs via OpenTelemetry (OTLP). Só exporta quando <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> está
    /// definido (no compose aponta para o Grafana); sem ele (testes, dotnet run) nada sai da máquina.
    /// Instrumenta: requisições recebidas, chamadas HTTP feitas, SQL Server, runtime do .NET e a fonte
    /// <see cref="SourceName"/> (publicação/consumo no Kafka e métricas de negócio).
    /// </summary>
    public static WebApplicationBuilder AddMarketplaceObservability(this WebApplicationBuilder builder, string serviceName)
    {
        var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceNamespace: "marketplace", serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddSource(SourceName)
                .AddAspNetCoreInstrumentation(options =>
                    // Health checks (o compose e o E2E consultam o tempo todo) só poluiriam os traces.
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(SourceName)
                .AddMeter("System.Runtime")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .UseOtlpExporter();

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        return builder;
    }
}
