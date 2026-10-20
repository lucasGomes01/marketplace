using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Intervalo de polling quando não há aviso de commit (eventos gravados por outra cópia do serviço).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 100;

    /// <summary>De quanto em quanto tempo uma instância sem o lock tenta assumir o relay.</summary>
    public TimeSpan LockRetryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Quanto tempo o histórico publicado fica na tabela.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}

/// <summary>
/// Publica no Kafka o que foi gravado no outbox (ADR-005). Só uma instância por serviço fica ativa (lock no SQL
/// Server); ela envia em ordem, para no primeiro erro e tenta de novo com backoff, sem pular a fila. As outras
/// esperam para assumir se a ativa cair.
/// </summary>
public sealed class OutboxRelay<TContext>(
    IServiceScopeFactory scopes,
    IOutboxLock outboxLock,
    OutboxSignal signal,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxRelay<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var lease = await outboxLock.TryAcquireAsync(stoppingToken);
                if (lease is null)
                {
                    await Task.Delay(_options.LockRetryInterval, stoppingToken);
                    continue;
                }

                logger.LogInformation("Outbox relay for {Context} is active on this instance", typeof(TContext).Name);
                await RelayWhileHoldingAsync(lease, stoppingToken);
                logger.LogWarning("Outbox relay for {Context} lost its lock", typeof(TContext).Name);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                // Banco fora, por exemplo: espera e tenta de novo do começo (pegar o lock).
                logger.LogError(ex, "Outbox relay for {Context} failed; retrying", typeof(TContext).Name);
                await DelayQuietlyAsync(_options.LockRetryInterval, stoppingToken);
            }
            finally
            {
                OutboxMetrics.Clear();
            }
        }
    }

    private async Task RelayWhileHoldingAsync(IOutboxLease lease, CancellationToken stoppingToken)
    {
        var failures = 0;
        var nextCleanup = clock.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested && await lease.IsHeldAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = new EfOutboxStore<TContext>(scope.ServiceProvider.GetRequiredService<TContext>());
            var processor = ActivatorUtilities.CreateInstance<OutboxProcessor>(scope.ServiceProvider, store);

            var result = await processor.ProcessBatchAsync(_options.BatchSize, stoppingToken);
            OutboxMetrics.Record(await store.GetStatsAsync(stoppingToken), clock.GetUtcNow());

            if (clock.GetUtcNow() >= nextCleanup)
            {
                var deleted = await store.DeletePublishedBeforeAsync(clock.GetUtcNow() - _options.Retention, stoppingToken);
                logger.LogDebug("Outbox cleanup removed {Count} published messages", deleted);
                nextCleanup = clock.GetUtcNow() + CleanupInterval;
            }

            if (result.Failed)
            {
                await Task.Delay(Backoff(++failures), stoppingToken);
            }
            else if (result.Published < _options.BatchSize)
            {
                failures = 0;
                await signal.WaitAsync(_options.PollInterval, stoppingToken);
            }
            // Lote cheio: provavelmente há mais pendentes, segue sem esperar.
        }
    }

    private static TimeSpan Backoff(int failures) =>
        TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, Math.Min(failures - 1, 5)), 10_000));

    private static async Task DelayQuietlyAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
