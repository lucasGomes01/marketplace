using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace BuildingBlocks.Messaging;

/// <summary>Envelope padrão de todos os eventos (contrato em <c>.ai/context/events.md</c>).</summary>
public sealed record EventEnvelope<T>(Guid EventId, string Type, DateTimeOffset OccurredAt, T Data);

public static class KafkaMessages
{
    public const string EventTypeHeader = "event-type";
    public const string EventIdHeader = "event-id";

    /// <summary>JSON camelCase, igual ao das APIs, para qualquer linguagem consumir (o Orders é em Go).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Message<string, string> Create<T>(string eventType, string key, Guid eventId, DateTimeOffset occurredAt, T data) =>
        Create(eventType, key, eventId, Serialize(eventType, eventId, occurredAt, data));

    /// <summary>Só o envelope JSON (o outbox grava isto e publica depois).</summary>
    public static string Serialize<T>(string eventType, Guid eventId, DateTimeOffset occurredAt, T data) =>
        JsonSerializer.Serialize(new EventEnvelope<T>(eventId, eventType, occurredAt, data), Json);

    /// <summary>Mensagem com um envelope já serializado por <see cref="Serialize{T}"/>.</summary>
    public static Message<string, string> Create(string eventType, string key, Guid eventId, string payload) => new()
    {
        Key = key,
        Value = payload,
        Headers =
        [
            new Header(EventTypeHeader, Encoding.UTF8.GetBytes(eventType)),
            new Header(EventIdHeader, Encoding.UTF8.GetBytes(eventId.ToString())),
        ],
    };
}
