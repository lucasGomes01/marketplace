namespace BuildingBlocks.Outbox;

/// <summary>
/// Evento esperando para ir ao Kafka. Gravado na mesma transação da mudança de negócio (ADR-005);
/// o <see cref="OutboxRelay{TContext}"/> publica e marca <see cref="PublishedAt"/>.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    /// <summary>Ordem de gravação (identity). O relay publica nesta ordem.</summary>
    public long Sequence { get; internal set; }

    /// <summary>Id do evento; único na tabela (o mesmo evento não é registrado duas vezes).</summary>
    public Guid EventId { get; private set; }

    /// <summary>Tipo do evento = nome do tópico.</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>Chave da mensagem no Kafka (id do agregado).</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Envelope JSON pronto, com o <c>occurredAt</c> da gravação.</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>W3C <c>traceparent</c> de quem gravou, para o trace continuar na publicação.</summary>
    public string? TraceParent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Null enquanto pendente.</summary>
    public DateTimeOffset? PublishedAt { get; internal set; }

    public int Attempts { get; internal set; }

    public string? LastError { get; internal set; }

    public static OutboxMessage Create(
        string type, string key, Guid eventId, string payload, string? traceParent, DateTimeOffset createdAt) => new()
    {
        Type = type,
        Key = key,
        EventId = eventId,
        Payload = payload,
        TraceParent = traceParent,
        CreatedAt = createdAt,
    };
}
