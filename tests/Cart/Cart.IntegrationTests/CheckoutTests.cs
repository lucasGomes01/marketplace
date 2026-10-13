using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;

namespace Cart.IntegrationTests;

[Collection(CartApiCollection.Name)]
public class CheckoutTests(CartApiFactory factory)
{
    private sealed record CheckoutBody(Guid CheckoutId, Guid OrderId);

    private sealed record CartBody(List<JsonElement> Items, int TotalQuantity, decimal Total, string Currency);

    /// <summary>Lê o tópico desde o início e devolve a mensagem cujo header event-id é o esperado.</summary>
    private Message<string, string>? Consume(string topic, Guid eventId)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = factory.KafkaBootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(1));
            if (result?.Message is { } message &&
                Encoding.UTF8.GetString(message.Headers.GetLastBytes("event-id")) == eventId.ToString())
            {
                return message;
            }
        }
        return null;
    }

    [Fact]
    public async Task Checkout_publishes_the_cart_to_kafka_and_empties_the_cart()
    {
        var (client, buyerId) = factory.CreateBuyerClient();
        var drill = factory.Catalog.Add(price: 199.90m, stock: 5, name: "Furadeira");
        var wrench = factory.Catalog.Add(price: 89.50m, stock: 5, name: "Chaves");
        await client.PostAsJsonAsync("/cart/items", new { productId = drill.Id, quantity = 2 });
        await client.PostAsJsonAsync("/cart/items", new { productId = wrench.Id, quantity = 1 });

        var response = await client.PostAsync("/cart/checkout", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var checkout = await response.Content.ReadFromJsonAsync<CheckoutBody>();
        Assert.Equal(checkout!.CheckoutId, checkout.OrderId);
        Assert.Equal($"/orders/{checkout.OrderId}", response.Headers.Location?.OriginalString);

        var message = Consume("cart.checked-out", checkout.CheckoutId);
        Assert.NotNull(message);
        using var envelope = JsonDocument.Parse(message.Value);
        var root = envelope.RootElement;
        Assert.Equal("cart.checked-out", root.GetProperty("type").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(buyerId, data.GetProperty("buyerId").GetGuid());
        Assert.Equal(data.GetProperty("cartId").GetGuid().ToString(), message.Key);
        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Furadeira", "Chaves"], items.Select(i => i.GetProperty("productName").GetString()));
        Assert.Equal((199.90m, 2, "BRL"), (items[0].GetProperty("unitPrice").GetDecimal(),
            items[0].GetProperty("quantity").GetInt32(), items[0].GetProperty("currency").GetString()));

        var cart = await client.GetFromJsonAsync<CartBody>("/cart");
        Assert.Empty(cart!.Items);
    }

    [Fact]
    public async Task Empty_cart_cannot_be_checked_out()
    {
        var (client, _) = factory.CreateBuyerClient();

        var response = await client.PostAsync("/cart/checkout", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("cart", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Checkout_requires_login()
    {
        var response = await factory.CreateClient().PostAsync("/cart/checkout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
