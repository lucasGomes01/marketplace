using BuildingBlocks.Messaging;

namespace BuildingBlocks.Outbox;

/// <summary>Envia pelo produtor Kafka de sempre (acks de todas as réplicas, idempotente).</summary>
internal sealed class KafkaOutboxTransport(KafkaEventPublisher kafka) : IOutboxTransport
{
    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        kafka.SendAsync(message.Type, message.Key, message.EventId, message.Payload, message.TraceParent, cancellationToken);
}
