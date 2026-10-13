using Catalog.Application.Abstractions;
using Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

internal sealed class ProductRepository(CatalogDbContext db) : IProductRepository
{
    public async Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> ListNewestFirstAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await db.Products.CountAsync(cancellationToken);
        var items = await db.Products.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
