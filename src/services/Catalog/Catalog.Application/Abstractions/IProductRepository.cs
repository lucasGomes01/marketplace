using Catalog.Domain;

namespace Catalog.Application.Abstractions;

/// <summary>Porta de persistência de produtos (EF Core na Infrastructure).</summary>
public interface IProductRepository
{
    Task AddAsync(Product product, CancellationToken cancellationToken);

    Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Página de produtos, mais recentes primeiro, e o total geral.</summary>
    Task<(IReadOnlyList<Product> Items, int TotalCount)> ListNewestFirstAsync(
        int page, int pageSize, CancellationToken cancellationToken);
}
