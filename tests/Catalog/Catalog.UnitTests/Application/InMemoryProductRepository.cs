using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.UnitTests.Application;

internal sealed class InMemoryProductRepository : IProductRepository
{
    public List<Product> Products { get; } = [];

    public ProductListFilter? LastFilter { get; private set; }

    public Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        Products.Add(product);
        return Task.CompletedTask;
    }

    public Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Products.SingleOrDefault(p => p.Id == id));

    public Task<(IReadOnlyList<Product> Items, int TotalCount)> ListAsync(
        ProductListFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        var matching = Products.Where(p =>
            (filter.Search is null
                || p.Name.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)
                || (p.Brand?.Contains(filter.Search, StringComparison.OrdinalIgnoreCase) ?? false))
            && (filter.Category is null || p.Category == filter.Category)
            && (filter.Condition is null || p.Condition == filter.Condition)
            && (filter.MinPrice is null || p.Price.Amount >= filter.MinPrice)
            && (filter.MaxPrice is null || p.Price.Amount <= filter.MaxPrice)
            && (!filter.FreeShippingOnly || p.FreeShipping)).ToList();

        var ordered = filter.Sort switch
        {
            ProductSort.PriceAscending => matching.OrderBy(p => p.Price.Amount).ThenByDescending(p => p.Id),
            ProductSort.PriceDescending => matching.OrderByDescending(p => p.Price.Amount).ThenByDescending(p => p.Id),
            ProductSort.Name => matching.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(p => p.Id),
            _ => matching.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        };
        IReadOnlyList<Product> items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, matching.Count));
    }
}
