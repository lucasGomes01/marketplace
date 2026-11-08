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

    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> ListAsync(
        ProductListFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking();

        // A collation padrão do SQL Server não diferencia maiúsculas: Contains já é a busca que se espera.
        if (filter.Search is { } search)
        {
            query = query.Where(p => p.Name.Contains(search) || (p.Brand != null && p.Brand.Contains(search)));
        }
        if (filter.Category is { } category)
        {
            query = query.Where(p => p.Category == category);
        }
        if (filter.Condition is { } condition)
        {
            query = query.Where(p => p.Condition == condition);
        }
        if (filter.MinPrice is { } min)
        {
            query = query.Where(p => p.Price.Amount >= min);
        }
        if (filter.MaxPrice is { } max)
        {
            query = query.Where(p => p.Price.Amount <= max);
        }
        if (filter.FreeShippingOnly)
        {
            query = query.Where(p => p.FreeShipping);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var ordered = filter.Sort switch
        {
            ProductSort.PriceAscending => query.OrderBy(p => p.Price.Amount).ThenByDescending(p => p.Id),
            ProductSort.PriceDescending => query.OrderByDescending(p => p.Price.Amount).ThenByDescending(p => p.Id),
            ProductSort.Name => query.OrderBy(p => p.Name).ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        };
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
