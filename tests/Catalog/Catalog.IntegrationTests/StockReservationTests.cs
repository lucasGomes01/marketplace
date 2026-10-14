using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Application.Messaging;
using Catalog.Application.Abstractions;
using Catalog.Application.ReserveStock;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.IntegrationTests;

[Collection(CatalogApiCollection.Name)]
public class StockReservationTests(CatalogApiFactory factory)
{
    private sealed record ProductBody(Guid Id, string Name, int Stock);

    private async Task<ProductBody> CreateProduct(string name, int stock)
    {
        var (seller, _) = factory.CreateSellerClient();
        var response = await seller.PostAsJsonAsync("/products", new { name, price = 10m, stock });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductBody>())!;
    }

    private Task<int> StockOf(Guid productId) =>
        factory.CreateClient().GetFromJsonAsync<ProductBody>($"/products/{productId}").ContinueWith(t => t.Result!.Stock);

    private async Task PublishOrderPlaced(Guid orderId, params (Guid ProductId, int Quantity)[] items)
    {
        var publisher = factory.Services.GetRequiredService<IEventPublisher>();
        var order = new OrderPlaced(orderId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            items.Select(i => new OrderPlacedItem(i.ProductId, i.Quantity)).ToList());
        await publisher.PublishAsync(OrderPlaced.EventType, orderId.ToString(), orderId, order, CancellationToken.None);
    }

    /// <summary>Todas as mensagens do tópico cujo header event-id é o esperado (lê por até <paramref name="seconds"/>).</summary>
    private List<JsonElement> Messages(string topic, Guid eventId, int expected = 1, int seconds = 30)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = factory.KafkaBootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);

        var found = new List<JsonElement>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && found.Count < expected)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result?.Message is { } message &&
                Encoding.UTF8.GetString(message.Headers.GetLastBytes("event-id")) == eventId.ToString())
            {
                found.Add(JsonDocument.Parse(message.Value).RootElement.GetProperty("data").Clone());
            }
        }
        return found;
    }

    [Fact]
    public async Task Order_placed_reserves_the_stock_and_publishes_stock_reserved()
    {
        var drill = await CreateProduct("Furadeira", 5);
        var orderId = Guid.CreateVersion7();

        await PublishOrderPlaced(orderId, (drill.Id, 2));

        var reserved = Assert.Single(Messages("stock.reserved", orderId));
        Assert.Equal(orderId, reserved.GetProperty("orderId").GetGuid());
        Assert.Equal(3, await StockOf(drill.Id));
    }

    [Fact]
    public async Task A_repeated_order_placed_does_not_reserve_twice()
    {
        var drill = await CreateProduct("Furadeira", 5);
        var orderId = Guid.CreateVersion7();

        await PublishOrderPlaced(orderId, (drill.Id, 2));
        await PublishOrderPlaced(orderId, (drill.Id, 2));

        Assert.Equal(2, Messages("stock.reserved", orderId, expected: 2).Count);
        Assert.Equal(3, await StockOf(drill.Id));
    }

    [Fact]
    public async Task Not_enough_stock_rejects_the_whole_order_with_a_reason()
    {
        var drill = await CreateProduct("Furadeira de impacto", 2);
        var wrench = await CreateProduct("Chaves", 10);
        var orderId = Guid.CreateVersion7();

        await PublishOrderPlaced(orderId, (wrench.Id, 1), (drill.Id, 3));

        var rejected = Assert.Single(Messages("stock.rejected", orderId));
        Assert.Equal("Estoque insuficiente: Furadeira de impacto (pedido 3, disponível 2)", rejected.GetProperty("reason").GetString());
        Assert.Equal([drill.Id], rejected.GetProperty("productIds").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Equal((2, 10), (await StockOf(drill.Id), await StockOf(wrench.Id)));
    }

    [Fact]
    public async Task Two_orders_for_the_last_unit_at_the_same_time_reserve_exactly_one()
    {
        var lastOne = await CreateProduct("Última unidade", 1);
        var orders = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };

        // Chama o handler direto, em paralelo, cada um no seu escopo — como dois consumidores ao mesmo tempo.
        // Conflito de concorrência é reprocessado, exatamente como o consumidor Kafka faz.
        await Task.WhenAll(orders.Select(orderId => Task.Run(async () =>
        {
            for (var attempt = 0; ; attempt++)
            {
                using var scope = factory.Services.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<OrderPlaced>>();
                try
                {
                    await handler.HandleAsync(orderId,
                        new OrderPlaced(orderId, Guid.CreateVersion7(), Guid.CreateVersion7(), [new OrderPlacedItem(lastOne.Id, 1)]),
                        CancellationToken.None);
                    return;
                }
                catch (StockConcurrencyException) when (attempt < 5)
                {
                }
            }
        })));

        // Confere no banco (a publicação já é coberta pelos outros testes; aqui o assunto é concorrência).
        using var check = factory.Services.CreateScope();
        var store = check.ServiceProvider.GetRequiredService<IStockStore>();
        var outcomes = new List<Catalog.Domain.ReservationOutcome>();
        foreach (var orderId in orders)
        {
            outcomes.Add((await store.FindReservationAsync(orderId, CancellationToken.None))!.Outcome);
        }
        Assert.Equal(
            [Catalog.Domain.ReservationOutcome.Reserved, Catalog.Domain.ReservationOutcome.Rejected],
            outcomes.Order());
        Assert.Equal(0, await StockOf(lastOne.Id));
    }
}
