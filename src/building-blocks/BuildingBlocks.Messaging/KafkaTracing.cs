using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Spans de publicação e consumo no Kafka, com o contexto do trace (W3C <c>traceparent</c>) nos headers da mensagem.
/// Assim uma compra aparece como um trace só: Cart → Orders (Go) → Catalog → Orders. Sem listener (OpenTelemetry
/// desligado, testes) as atividades não são criadas e os métodos devolvem null.
/// </summary>
public static class KafkaTracing
{
    /// <summary>Mesmo nome registrado no OpenTelemetry (BuildingBlocks.Observability).</summary>
    public const string SourceName = "Marketplace";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>Abre o span de envio e grava o contexto dele nos headers.</summary>
    /// <param name="parentTraceParent">
    /// <c>traceparent</c> de quem registrou o evento, quando a publicação acontece depois e fora da requisição
    /// (relay do outbox). Sem ele, o pai é a atividade atual.
    /// </param>
    public static Activity? StartPublish(string topic, Headers headers, string? parentTraceParent = null)
    {
        var activity = ActivityContext.TryParse(parentTraceParent, null, isRemote: false, out var parent)
            ? Source.StartActivity($"{topic} publish", ActivityKind.Producer, parent)
            : Source.StartActivity($"{topic} publish", ActivityKind.Producer);
        if (activity is not null)
        {
            Tag(activity, topic, "send");
            DistributedContextPropagator.Current.Inject(activity, headers, static (carrier, name, value) =>
            {
                if (carrier is Headers h && value is not null)
                {
                    h.Remove(name);
                    h.Add(name, Encoding.UTF8.GetBytes(value));
                }
            });
        }
        return activity;
    }

    /// <summary>Abre o span de processamento como filho do span de quem publicou (se a mensagem trouxer o contexto).</summary>
    public static Activity? StartProcess(string topic, Headers headers)
    {
        DistributedContextPropagator.Current.ExtractTraceIdAndState(headers, static (object? carrier, string name, out string? value, out IEnumerable<string>? values) =>
        {
            values = null;
            value = carrier is Headers h && h.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
        }, out var traceParent, out var traceState);

        ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var parent);
        var activity = Source.StartActivity($"{topic} process", ActivityKind.Consumer, parent);
        if (activity is not null)
        {
            Tag(activity, topic, "process");
        }
        return activity;
    }

    private static void Tag(Activity activity, string topic, string operation) => activity
        .SetTag("messaging.system", "kafka")
        .SetTag("messaging.destination.name", topic)
        .SetTag("messaging.operation.type", operation);
}
