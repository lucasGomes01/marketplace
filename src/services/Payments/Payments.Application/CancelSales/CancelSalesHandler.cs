using BuildingBlocks.Application.Messaging;
using Payments.Application.Abstractions;

namespace Payments.Application.CancelSales;

/// <summary>Evento consumido (publicado pelo Catalog): o pedido foi recusado por falta de estoque.</summary>
public sealed record StockRejected(Guid OrderId, string Reason, IReadOnlyList<Guid> ProductIds)
{
    public const string EventType = "stock.rejected";
}

/// <summary>Pedido recusado → os repasses dele são cancelados (nada foi vendido). Idempotente.</summary>
public sealed class CancelSalesHandler(IPayoutRepository payouts, TimeProvider clock) : IIntegrationEventHandler<StockRejected>
{
    public async Task HandleAsync(Guid eventId, StockRejected rejected, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var payout in await payouts.ListByOrderAsync(rejected.OrderId, cancellationToken))
        {
            changed |= payout.Cancel(string.IsNullOrWhiteSpace(rejected.Reason) ? "Pedido recusado" : rejected.Reason, clock);
        }

        if (changed)
        {
            await payouts.SaveChangesAsync(cancellationToken);
        }
    }
}
