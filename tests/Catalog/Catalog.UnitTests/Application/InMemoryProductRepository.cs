using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.UnitTests.Application;

internal sealed class InMemoryProductRepository : IProductRepository
{
    public List<Product> Products { get; } = [];

    public Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        Products.Add(product);
        return Task.CompletedTask;
    }

    public Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Products.SingleOrDefault(p => p.Id == id));

    public Task<(IReadOnlyList<Product> Items, int TotalCount)> ListNewestFirstAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        IReadOnlyList<Product> items = Products
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, Products.Count));
    }
}
