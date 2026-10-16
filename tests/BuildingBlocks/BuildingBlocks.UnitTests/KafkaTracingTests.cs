using System.Diagnostics;
using System.Text;
using BuildingBlocks.Messaging;
using Confluent.Kafka;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// O trace de uma compra atravessa o Kafka: quem publica grava o <c>traceparent</c> (W3C) nos headers e quem consome
/// continua o mesmo trace. Sem isso, cada serviço teria um trace solto.
/// </summary>
public sealed class KafkaTracingTests : IDisposable
{
    // Sem um listener as atividades não são criadas (é o que o OpenTelemetry faz em produção).
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = source => source.Name == KafkaTracing.SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public KafkaTracingTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose() => _listener.Dispose();

    private static string? Header(Headers headers, string name) =>
        headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    [Fact]
    public void Publishing_writes_the_traceparent_of_the_producer_span()
    {
        var message = KafkaMessages.Create("order.placed", "k", Guid.NewGuid(), DateTimeOffset.UtcNow, new { });

        using var producer = KafkaTracing.StartPublish("order.placed", message.Headers);

        Assert.NotNull(producer);
        Assert.Equal(ActivityKind.Producer, producer.Kind);
        var traceparent = Header(message.Headers, "traceparent");
        Assert.NotNull(traceparent);
        Assert.Contains(producer.TraceId.ToString(), traceparent);
        Assert.Contains(producer.SpanId.ToString(), traceparent);
    }

    [Fact]
    public void Consuming_continues_the_trace_from_the_headers()
    {
        var message = KafkaMessages.Create("order.placed", "k", Guid.NewGuid(), DateTimeOffset.UtcNow, new { });
        ActivityTraceId traceId;
        ActivitySpanId producerSpan;
        using (var producer = KafkaTracing.StartPublish("order.placed", message.Headers)!)
        {
            traceId = producer.TraceId;
            producerSpan = producer.SpanId;
        }

        using var consumer = KafkaTracing.StartProcess("order.placed", message.Headers);

        Assert.NotNull(consumer);
        Assert.Equal(ActivityKind.Consumer, consumer.Kind);
        Assert.Equal(traceId, consumer.TraceId);
        Assert.Equal(producerSpan, consumer.ParentSpanId);
        Assert.Equal("order.placed", consumer.GetTagItem("messaging.destination.name"));
    }

    [Fact]
    public void Message_without_traceparent_starts_a_new_trace()
    {
        using var consumer = KafkaTracing.StartProcess("order.placed", []);

        Assert.NotNull(consumer);
        Assert.Equal(default, consumer.ParentSpanId);
    }
}
