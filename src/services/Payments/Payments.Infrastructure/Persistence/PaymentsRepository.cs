using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

/// <summary>As duas portas sobre o mesmo DbContext (scoped): um SaveChanges grava repasses e cartão juntos.</summary>
internal sealed class PaymentsRepository(PaymentsDbContext db) : IPayoutRepository, IPayoutCardRepository
{
    private static readonly int[] UniqueViolationErrors = [2601, 2627];

    public Task<Payout?> FindAsync(Guid orderId, Guid sellerId, CancellationToken cancellationToken) =>
        db.Payouts.SingleOrDefaultAsync(p => p.OrderId == orderId && p.SellerId == sellerId, cancellationToken);

    public async Task<IReadOnlyList<Payout>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await db.Payouts.Where(p => p.OrderId == orderId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payout>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken) =>
        await db.Payouts.AsNoTracking()
            .Where(p => p.SellerId == sellerId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payout>> ListWaitingForCardAsync(Guid sellerId, CancellationToken cancellationToken) =>
        await db.Payouts
            .Where(p => p.SellerId == sellerId && (p.Status == PayoutStatus.AwaitingCard || p.Status == PayoutStatus.Failed))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payout>> ListAwaitingCardWithCardAsync(int limit, CancellationToken cancellationToken) =>
        await db.Payouts
            .Where(p => p.Status == PayoutStatus.AwaitingCard && db.PayoutCards.Any(c => c.SellerId == p.SellerId))
            .OrderBy(p => p.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payout>> ListDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await db.Payouts
            .Where(p => p.Status == PayoutStatus.Scheduled && p.ScheduledFor <= now)
            .OrderBy(p => p.ScheduledFor)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(Payout payout) => db.Payouts.Add(payout);

    public Task<PayoutCard?> FindAsync(Guid sellerId, CancellationToken cancellationToken) =>
        db.PayoutCards.SingleOrDefaultAsync(c => c.SellerId == sellerId, cancellationToken);

    public void Add(PayoutCard card) => db.PayoutCards.Add(card);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Descarta o que não foi gravado para as próximas operações do mesmo escopo não repetirem o conflito.
            db.ChangeTracker.Clear();
            throw new PayoutConcurrencyException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationErrors.Contains(sql.Number))
        {
            db.ChangeTracker.Clear();
            throw new DuplicatePayoutException();
        }
    }
}
