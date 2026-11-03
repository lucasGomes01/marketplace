using BuildingBlocks.Application;
using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.GetPayouts;

public sealed record GetSellerPayoutsQuery(Guid SellerId);

/// <summary>Totais por moeda: a receber (ainda não pago) e recebido.</summary>
public sealed record PayoutTotals(string Currency, decimal Pending, decimal Sent);

public sealed record SellerPayoutsResponse(PayoutCardResponse? Card, IReadOnlyList<PayoutTotals> Totals, IReadOnlyList<PayoutResponse> Payouts);

/// <summary>Painel de repasses do vendedor: cartão cadastrado, totais e repasses (mais recentes primeiro).</summary>
public sealed class GetSellerPayoutsHandler(IPayoutRepository payouts, IPayoutCardRepository cards)
    : IHandler<GetSellerPayoutsQuery, SellerPayoutsResponse>
{
    private static readonly PayoutStatus[] Pending =
        [PayoutStatus.AwaitingDelivery, PayoutStatus.AwaitingCard, PayoutStatus.Scheduled, PayoutStatus.Failed];

    public async Task<Result<SellerPayoutsResponse>> HandleAsync(GetSellerPayoutsQuery query, CancellationToken cancellationToken = default)
    {
        var card = await cards.FindAsync(query.SellerId, cancellationToken);
        var list = await payouts.ListBySellerAsync(query.SellerId, cancellationToken);

        var totals = list
            .GroupBy(p => p.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new PayoutTotals(
                g.Key,
                g.Where(p => Pending.Contains(p.Status)).Sum(p => p.NetAmount),
                g.Where(p => p.Status == PayoutStatus.Sent).Sum(p => p.NetAmount)))
            .ToList();

        return new SellerPayoutsResponse(
            card is null ? null : PayoutCardResponse.From(card), totals, list.Select(PayoutResponse.From).ToList());
    }
}
