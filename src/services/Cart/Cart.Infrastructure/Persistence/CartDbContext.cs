using BuildingBlocks.Outbox;
using Cart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cart.Infrastructure.Persistence;

public sealed class CartDbContext(DbContextOptions<CartDbContext> options) : DbContext(options)
{
    public DbSet<ShoppingCart> Carts => Set<ShoppingCart>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CartDbContext).Assembly).AddOutbox();
}
