using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

internal sealed class EfOutboxStore<TContext>(TContext db) : IOutboxStore
    where TContext : DbContext
{
    private DbSet<OutboxMessage> Outbox => db.Set<OutboxMessage>();

    public async Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int max, CancellationToken cancellationToken) =>
        await Outbox.AsNoTracking()
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.Sequence)
            .Take(max)
            .ToListAsync(cancellationToken);

    // Pela lista de sequências, nunca por faixa: uma transação mais lenta pode gravar uma sequência menor depois que o
    // lote foi lido, e ela ainda não foi enviada.
    public Task MarkPublishedAsync(IReadOnlyCollection<long> sequences, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        Outbox.Where(m => sequences.Contains(m.Sequence))
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.PublishedAt, publishedAt), cancellationToken);

    public Task MarkFailedAsync(long sequence, string error, CancellationToken cancellationToken)
    {
        var lastError = error.Length > 2000 ? error[..2000] : error;
        return Outbox.Where(m => m.Sequence == sequence)
            .ExecuteUpdateAsync(set => set
                .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                .SetProperty(m => m.LastError, lastError), cancellationToken);
    }

    public async Task<OutboxStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        var pending = Outbox.Where(m => m.PublishedAt == null);
        var count = await pending.CountAsync(cancellationToken);
        var oldest = count == 0 ? null : await pending.MinAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken);
        return new OutboxStats(count, oldest);
    }

    public Task<int> DeletePublishedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Outbox.Where(m => m.PublishedAt != null && m.PublishedAt < before).ExecuteDeleteAsync(cancellationToken);
}
