using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payments.Application.DispatchPayouts;

namespace Payments.Infrastructure.Workers;

/// <summary>De tempos em tempos, roda uma rodada do despachante de repasses (ver <see cref="DispatchDuePayoutsHandler"/>).</summary>
internal sealed class PayoutDispatcherWorker(
    IServiceScopeFactory scopes, TimeSpan interval, TimeProvider clock, ILogger<PayoutDispatcherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IHandler<DispatchDuePayoutsCommand, DispatchResult>>();
                var result = (await handler.HandleAsync(new DispatchDuePayoutsCommand(), stoppingToken)).Value;
                if (result is not { Sent: 0, Declined: 0, RetryLater: 0, Rescheduled: 0 })
                {
                    logger.LogInformation("Payouts dispatched: {Sent} sent, {Declined} declined, {Retry} to retry, {Rescheduled} rescheduled",
                        result.Sent, result.Declined, result.RetryLater, result.Rescheduled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Banco fora do ar etc.: tenta na próxima rodada.
                logger.LogError(ex, "Payout dispatch round failed");
            }
        }
        while (await NextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> NextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
