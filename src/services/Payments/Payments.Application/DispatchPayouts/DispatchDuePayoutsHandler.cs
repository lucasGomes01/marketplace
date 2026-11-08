using System.Diagnostics.Metrics;
using BuildingBlocks.Application;
using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.DispatchPayouts;

public sealed record DispatchDuePayoutsCommand;

public sealed record DispatchResult(int Sent, int Declined, int RetryLater, int Rescheduled);

/// <summary>
/// Uma rodada do despachante (chamado em segundo plano): agenda os que esperavam cartão e já têm, e envia à
/// Mastercard os repasses vencidos. Cada repasse é gravado logo depois do envio; a referência é sempre a mesma,
/// então reenviar depois de uma queda não paga duas vezes.
/// </summary>
public sealed class DispatchDuePayoutsHandler(
    IPayoutRepository payouts,
    IPayoutCardRepository cards,
    ICardProtector protector,
    IPayoutGateway gateway,
    PayoutPolicy policy,
    TimeProvider clock) : IHandler<DispatchDuePayoutsCommand, DispatchResult>
{
    private static readonly Meter Meter = new("Marketplace");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "marketplace.payouts", description: "Tentativas de repasse por resultado: sent, declined ou retry.");

    public async Task<Result<DispatchResult>> HandleAsync(DispatchDuePayoutsCommand command, CancellationToken cancellationToken = default)
    {
        var rescheduled = await ScheduleThoseWithCardAsync(cancellationToken);
        int sent = 0, declined = 0, retry = 0;

        foreach (var payout in await payouts.ListDueAsync(clock.GetUtcNow(), policy.BatchSize, cancellationToken))
        {
            var outcome = await SendAsync(payout, cancellationToken);
            try
            {
                await payouts.SaveChangesAsync(cancellationToken);
            }
            catch (PayoutConcurrencyException)
            {
                continue;
            }
            Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
            _ = outcome switch
            {
                "sent" => sent++,
                "declined" => declined++,
                _ => retry++,
            };
        }

        return new DispatchResult(sent, declined, retry, rescheduled);
    }

    private async Task<int> ScheduleThoseWithCardAsync(CancellationToken cancellationToken)
    {
        var waiting = await payouts.ListAwaitingCardWithCardAsync(policy.BatchSize, cancellationToken);
        foreach (var payout in waiting)
        {
            payout.CardAvailable(policy.HoldPeriod, clock);
        }
        if (waiting.Count > 0)
        {
            await payouts.SaveChangesAsync(cancellationToken);
        }
        return waiting.Count;
    }

    private async Task<string> SendAsync(Payout payout, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(payout.SellerId, cancellationToken);
        if (card is null)
        {
            payout.RetryLater("Seller has no payout card.", policy.RetryDelay, clock);
            return "retry";
        }
        if (card.Expiry.IsExpired(clock))
        {
            payout.MarkDeclined("Cartão de recebimento vencido.", clock);
            return "declined";
        }

        try
        {
            var transfer = new PayoutTransfer(
                payout.TransferReference, payout.Net, protector.Unprotect(card.ProtectedNumber), card.Expiry, card.HolderName);
            var result = await gateway.SendAsync(transfer, cancellationToken);
            if (result.Approved)
            {
                payout.MarkSent(result.ProviderReference!, clock);
                return "sent";
            }
            payout.MarkDeclined(result.DeclineReason ?? "Recusado.", clock);
            return "declined";
        }
        catch (PayoutGatewayUnavailableException ex)
        {
            payout.RetryLater(ex.Message, policy.RetryDelay, clock);
            return "retry";
        }
    }
}
