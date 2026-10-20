using System.Diagnostics.Metrics;
using BuildingBlocks.Application.Messaging;
using Catalog.Application.Abstractions;
using Catalog.Application.ReserveStock;
using Catalog.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.UnitTests.Application;

internal sealed class FakeStockStore : IStockStore
{
    public Dictionary<Guid, Product> Products { get; } = [];
    public Dictionary<Guid, StockReservation> Reservations { get; } = [];
    public bool SimulateConcurrencyConflict { get; set; }

    /// <summary>Chamado no começo de cada gravação (ex.: para ver o que já tinha sido registrado no outbox).</summary>
    public Action? BeforeSave { get; set; }

    public Task<StockReservation?> FindReservationAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Reservations.GetValueOrDefault(orderId));

    public Task<IReadOnlyList<Product>> FindProductsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(productIds.Where(Products.ContainsKey).Select(id => Products[id]).ToList());

    public Task SaveAsync(StockReservation reservation, CancellationToken cancellationToken)
    {
        BeforeSave?.Invoke();
        if (SimulateConcurrencyConflict)
        {
            throw new StockConcurrencyException();
        }
        Reservations[reservation.OrderId] = reservation;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingPublisher : IEventPublisher
{
    public List<(string EventType, string Key, Guid EventId, object Data)> Published { get; } = [];

    public Task PublishAsync<T>(string eventType, string key, Guid eventId, T data, CancellationToken cancellationToken)
    {
        Published.Add((eventType, key, eventId, data!));
        return Task.CompletedTask;
    }
}

public class ReserveStockHandlerTests
{
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private readonly FakeStockStore _store = new();
    private readonly RecordingPublisher _events = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    private ReserveStockHandler Handler => new(_store, _events, _clock);

    private Product AddProduct(string name, int stock)
    {
        var product = Product.Create(Guid.CreateVersion7(), name, null, Money.Create(10m), stock, _clock);
        _store.Products[product.Id] = product;
        return product;
    }

    private static OrderPlaced Order(params (Product Product, int Quantity)[] items) =>
        new(OrderId, Guid.CreateVersion7(), Guid.CreateVersion7(), items.Select(i => new OrderPlacedItem(i.Product.Id, i.Quantity)).ToList());

    private Task Handle(OrderPlaced order) => Handler.HandleAsync(order.OrderId, order, CancellationToken.None);

    [Fact]
    public async Task Reserves_every_item_and_publishes_stock_reserved()
    {
        var drill = AddProduct("Furadeira", 5);
        var wrench = AddProduct("Chaves", 2);

        await Handle(Order((drill, 3), (wrench, 2)));

        Assert.Equal((2, 0), (drill.Stock, wrench.Stock));
        Assert.Equal(ReservationOutcome.Reserved, _store.Reservations[OrderId].Outcome);
        var (eventType, key, eventId, data) = Assert.Single(_events.Published);
        Assert.Equal(("stock.reserved", OrderId.ToString(), OrderId), (eventType, key, eventId));
        Assert.Equal(new StockReserved(OrderId), data);
    }

    [Fact]
    public async Task One_item_without_stock_rejects_the_whole_order_and_reserves_nothing()
    {
        var drill = AddProduct("Furadeira de impacto 700W", 2);
        var wrench = AddProduct("Chaves", 10);

        await Handle(Order((wrench, 1), (drill, 3)));

        Assert.Equal((10, 2), (wrench.Stock, drill.Stock));
        var rejected = Assert.IsType<StockRejected>(Assert.Single(_events.Published).Data);
        Assert.Equal(OrderId, rejected.OrderId);
        Assert.Equal([drill.Id], rejected.ProductIds);
        Assert.Equal("Estoque insuficiente: Furadeira de impacto 700W (pedido 3, disponível 2)", rejected.Reason);
        Assert.Equal(ReservationOutcome.Rejected, _store.Reservations[OrderId].Outcome);
    }

    [Fact]
    public async Task A_product_that_no_longer_exists_rejects_the_order()
    {
        var drill = AddProduct("Furadeira", 5);
        var gone = Product.Create(Guid.CreateVersion7(), "Removido", null, Money.Create(1m), 1, _clock);

        await Handle(Order((drill, 1), (gone, 1)));

        var rejected = Assert.IsType<StockRejected>(Assert.Single(_events.Published).Data);
        Assert.Equal([gone.Id], rejected.ProductIds);
        Assert.Contains("indisponível", rejected.Reason);
        Assert.Equal(5, drill.Stock);
    }

    [Fact]
    public async Task The_outcome_is_registered_before_saving_so_reservation_and_event_go_in_one_transaction()
    {
        // Com o outbox, "publicar" só registra o evento no DbContext; ele é gravado no SaveChanges da reserva.
        var drill = AddProduct("Furadeira", 5);
        var registeredWhenSaving = -1;
        _store.BeforeSave = () => registeredWhenSaving = _events.Published.Count;

        await Handle(Order((drill, 1)));

        Assert.Equal(1, registeredWhenSaving);
    }

    [Theory]
    [InlineData(5, 2)] // reservado
    [InlineData(1, 5)] // recusado
    public async Task A_repeated_event_neither_reserves_again_nor_registers_the_outcome_again(int stock, int quantity)
    {
        // O resultado já está no outbox (publicado ou pendente) desde a primeira vez.
        var drill = AddProduct("Furadeira", stock);
        var order = Order((drill, quantity));

        await Handle(order);
        var stockAfterFirst = drill.Stock;
        await Handle(order);

        Assert.Equal(stockAfterFirst, drill.Stock);
        Assert.Single(_events.Published);
    }

    [Fact]
    public async Task A_concurrency_conflict_propagates_so_the_event_is_retried()
    {
        // A transação não é confirmada: nem a baixa nem o evento registrado ficam gravados.
        var drill = AddProduct("Furadeira", 1);
        _store.SimulateConcurrencyConflict = true;

        await Assert.ThrowsAsync<StockConcurrencyException>(() => Handle(Order((drill, 1))));

        Assert.Empty(_store.Reservations);
    }

    [Fact]
    public async Task Counts_each_new_reservation_by_outcome_for_the_dashboards()
    {
        var recorded = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Name == "marketplace.stock.reservations") l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome") recorded.Add($"{tag.Value}:{value}");
            }
        });
        listener.Start();
        var drill = AddProduct("Furadeira", 1);
        var reserved = Order((drill, 1));
        var rejected = Order((drill, 1)) with { OrderId = Guid.CreateVersion7() };

        await Handle(reserved);
        await Handle(reserved); // repetido: não conta de novo
        await Handle(rejected);

        Assert.Equal(["reserved:1", "rejected:1"], recorded);
    }

    [Fact]
    public async Task An_order_without_items_is_an_invalid_event()
    {
        await Assert.ThrowsAsync<InvalidEventException>(() => Handle(Order()));
    }
}
