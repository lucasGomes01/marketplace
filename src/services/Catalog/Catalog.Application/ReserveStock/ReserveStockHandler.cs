using System.Diagnostics.Metrics;
using BuildingBlocks.Application.Messaging;
using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application.ReserveStock;

/// <summary>Evento consumido (publicado pelo Orders). Contrato em <c>.ai/context/events.md</c>.</summary>
public sealed record OrderPlaced(Guid OrderId, Guid BuyerId, Guid CartId, IReadOnlyList<OrderPlacedItem> Items)
{
    public const string EventType = "order.placed";
}

public sealed record OrderPlacedItem(Guid ProductId, int Quantity);

public sealed record StockReserved(Guid OrderId)
{
    public const string EventType = "stock.reserved";
}

public sealed record StockRejected(Guid OrderId, string Reason, IReadOnlyList<Guid> ProductIds)
{
    public const string EventType = "stock.rejected";
}

/// <summary>
/// Baixa o estoque de um pedido, tudo ou nada, e publica o resultado. Idempotente pela reserva gravada junto com a
/// baixa. Conflito de concorrência (última unidade disputada) sobe como exceção → o consumidor reprocessa.
/// </summary>
public sealed class ReserveStockHandler(IStockStore store, IEventPublisher events, TimeProvider clock)
    : IIntegrationEventHandler<OrderPlaced>
{
    // Métrica de negócio para os painéis (exportada pelo OpenTelemetry quando ligado; fonte "Marketplace").
    private static readonly Meter Meter = new("Marketplace");
    private static readonly Counter<long> Reservations = Meter.CreateCounter<long>(
        "marketplace.stock.reservations", description: "Reservas de estoque por resultado: reserved ou rejected.");

    public async Task HandleAsync(Guid eventId, OrderPlaced order, CancellationToken cancellationToken)
    {
        if (order.Items is not { Count: > 0 })
        {
            throw new InvalidEventException($"Order {order.OrderId} has no items.");
        }

        var reservation = await store.FindReservationAsync(order.OrderId, cancellationToken);
        if (reservation is null)
        {
            reservation = await ReserveAsync(order, cancellationToken);
            await store.SaveAsync(reservation, cancellationToken);
            Reservations.Add(1, new KeyValuePair<string, object?>("outcome", reservation.Outcome.ToString().ToLowerInvariant()));
        }

        await PublishAsync(reservation, cancellationToken);
    }

    private async Task<StockReservation> ReserveAsync(OrderPlaced order, CancellationToken cancellationToken)
    {
        var requested = order.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var products = (await store.FindProductsAsync(requested.Keys, cancellationToken)).ToDictionary(p => p.Id);

        var problems = new List<(Guid ProductId, string Message)>();
        foreach (var (productId, quantity) in requested)
        {
            if (!products.TryGetValue(productId, out var product))
            {
                problems.Add((productId, "Produto indisponível (removido do catálogo)"));
            }
            else if (product.Stock < quantity)
            {
                problems.Add((productId, $"Estoque insuficiente: {product.Name} (pedido {quantity}, disponível {product.Stock})"));
            }
        }

        if (problems.Count > 0)
        {
            return StockReservation.Rejected(order.OrderId, string.Join("; ", problems.Select(p => p.Message)),
                problems.Select(p => p.ProductId), clock);
        }

        foreach (var (productId, quantity) in requested)
        {
            products[productId].RemoveStock(quantity, clock);
        }
        return StockReservation.Reserved(order.OrderId, clock);
    }

    // Um resultado por pedido: o id do evento é o id do pedido (consumidores ignoram repetidos).
    private Task PublishAsync(StockReservation reservation, CancellationToken cancellationToken) =>
        reservation.Outcome == ReservationOutcome.Reserved
            ? events.PublishAsync(StockReserved.EventType, reservation.OrderId.ToString(), reservation.OrderId,
                new StockReserved(reservation.OrderId), cancellationToken)
            : events.PublishAsync(StockRejected.EventType, reservation.OrderId.ToString(), reservation.OrderId,
                new StockRejected(reservation.OrderId, reservation.Reason!, reservation.RejectedProductIds), cancellationToken);
}
