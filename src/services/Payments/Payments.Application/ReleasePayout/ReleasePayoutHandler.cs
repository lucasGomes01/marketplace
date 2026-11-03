using BuildingBlocks.Application.Messaging;
using Payments.Application.Abstractions;

namespace Payments.Application.ReleasePayout;

/// <summary>Evento consumido (publicado pelo Orders) a cada etapa do envio de um vendedor.</summary>
public sealed record ShipmentStatusChanged(Guid OrderId, Guid BuyerId, Guid SellerId, string Status, DateTimeOffset ChangedAt)
{
    public const string EventType = "shipment.status-changed";
    public const string Delivered = "Delivered";
}

/// <summary>
/// O comprador confirmou a entrega ("Recebi") → o repasse daquele vendedor é liberado (agendado após a carência) ou
/// fica esperando o cartão. Outras etapas são ignoradas. Idempotente.
/// </summary>
public sealed class ReleasePayoutHandler(
    IPayoutRepository payouts, IPayoutCardRepository cards, PayoutPolicy policy, TimeProvider clock)
    : IIntegrationEventHandler<ShipmentStatusChanged>
{
    public async Task HandleAsync(Guid eventId, ShipmentStatusChanged change, CancellationToken cancellationToken)
    {
        if (change.Status != ShipmentStatusChanged.Delivered)
        {
            return;
        }

        // A venda nasce do cart.checked-out (outro tópico). Se ainda não chegou, exceção = nova tentativa com backoff.
        var payout = await payouts.FindAsync(change.OrderId, change.SellerId, cancellationToken)
            ?? throw new SaleNotRecordedYetException(change.OrderId, change.SellerId);

        var hasCard = await cards.FindAsync(change.SellerId, cancellationToken) is not null;
        if (payout.MarkDelivered(change.ChangedAt, hasCard, policy.HoldPeriod, clock))
        {
            await payouts.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class SaleNotRecordedYetException(Guid orderId, Guid sellerId)
    : Exception($"No sale recorded yet for order {orderId} and seller {sellerId}; retrying.");
