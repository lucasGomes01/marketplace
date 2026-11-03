using Microsoft.EntityFrameworkCore;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payout> Payouts => Set<Payout>();

    public DbSet<PayoutCard> PayoutCards => Set<PayoutCard>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
}
