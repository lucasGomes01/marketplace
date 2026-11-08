using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;

namespace BuildingBlocks.Messaging;

/// <summary>Envelope padrão de todos os eventos (contrato em <c>.ai/context/events.md</c>).</summary>
/// <param name="EventId">
/// GUID. Ids em texto vindos de outros produtores (o Orders usa <c>orderId:sellerId:status</c> no
/// <c>shipment.status-changed</c>) viram um GUID estável na leitura (ver <see cref="EventIdConverter"/>).
/// </param>
public sealed record EventEnvelope<T>(
    [property: JsonConverter(typeof(EventIdConverter))] Guid EventId, string Type, DateTimeOffset OccurredAt, T Data);

/// <summary>
/// Lê o <c>eventId</c> do envelope: GUID como está; qualquer outro texto vira um GUID determinístico (SHA-256 do
/// texto, versão 8 do RFC 9562), para a idempotência dos consumidores continuar funcionando. Escreve sempre GUID.
/// </summary>
public sealed class EventIdConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("eventId is required.");
        return Guid.TryParse(text, out var id) ? id : FromText(text);
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    private static Guid FromText(string text)
    {
        Span<byte> bytes = stackalloc byte[16];
        SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // versão 8 (personalizada)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variante RFC
        return new Guid(bytes, bigEndian: true);
    }
}

public static class KafkaMessages
{
    public const string EventTypeHeader = "event-type";
    public const string EventIdHeader = "event-id";

    /// <summary>JSON camelCase, igual ao das APIs, para qualquer linguagem consumir (o Orders é em Go).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Message<string, string> Create<T>(string eventType, string key, Guid eventId, DateTimeOffset occurredAt, T data) =>
        Create(eventType, key, eventId, Serialize(eventType, eventId, occurredAt, data));

    /// <summary>Lê um envelope recebido do Kafka (aceita <c>eventId</c> que não é GUID, ver <see cref="EventIdConverter"/>).</summary>
    public static EventEnvelope<T>? Deserialize<T>(string json) => JsonSerializer.Deserialize<EventEnvelope<T>>(json, Json);

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
