using System.Text;
using System.Text.Json;
using BuildingBlocks.Messaging;

namespace BuildingBlocks.UnitTests;

public class KafkaMessagesTests
{
    private sealed record SampleEvent(Guid OrderId, decimal UnitPrice, IReadOnlyList<string> ProductNames);

    private static readonly Guid EventId = Guid.Parse("01a13700-0000-7000-8000-000000000001");
    private static readonly Guid OrderId = Guid.Parse("01a13700-0000-7000-8000-0000000000aa");
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 13, 18, 30, 0, TimeSpan.Zero);

    private static Confluent.Kafka.Message<string, string> Create() =>
        KafkaMessages.Create("order.placed", key: "order-key", EventId, OccurredAt,
            new SampleEvent(OrderId, 10.50m, ["Furadeira", "Peças"]));

    [Fact]
    public void Key_is_the_aggregate_id_given()
    {
        Assert.Equal("order-key", Create().Key);
    }

    [Fact]
    public void Value_is_the_camelCase_envelope()
    {
        using var json = JsonDocument.Parse(Create().Value);
        var root = json.RootElement;

        Assert.Equal(EventId, root.GetProperty("eventId").GetGuid());
        Assert.Equal("order.placed", root.GetProperty("type").GetString());
        Assert.Equal(OccurredAt, root.GetProperty("occurredAt").GetDateTimeOffset());
        var data = root.GetProperty("data");
        Assert.Equal(OrderId, data.GetProperty("orderId").GetGuid());
        Assert.Equal(10.50m, data.GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Peças", data.GetProperty("productNames")[1].GetString());
    }

    [Fact]
    public void Headers_carry_type_and_id_to_filter_without_parsing()
    {
        var headers = Create().Headers.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()));

        Assert.Equal("order.placed", headers["event-type"]);
        Assert.Equal(EventId.ToString(), headers["event-id"]);
    }

    [Fact]
    public void An_envelope_serialized_ahead_of_time_becomes_the_same_message()
    {
        // O outbox grava o envelope pronto e o relay só monta a mensagem depois.
        var payload = KafkaMessages.Serialize("order.placed", EventId, OccurredAt,
            new SampleEvent(OrderId, 10.50m, ["Furadeira", "Peças"]));

        var message = KafkaMessages.Create("order.placed", key: "order-key", EventId, payload);

        var direct = Create();
        Assert.Equal(direct.Key, message.Key);
        Assert.Equal(direct.Value, message.Value);
        Assert.Equal(
            direct.Headers.Select(h => (h.Key, Encoding.UTF8.GetString(h.GetValueBytes()))),
            message.Headers.Select(h => (h.Key, Encoding.UTF8.GetString(h.GetValueBytes()))));
    }
}
