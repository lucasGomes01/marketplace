using Catalog.Domain;

namespace Catalog.Application.Abstractions;

public enum ProductSort
{
    Newest,
    PriceAscending,
    PriceDescending,
    Name,
}

/// <summary>Filtros da vitrine. Nulo = sem filtro.</summary>
public sealed record ProductListFilter
{
    public static readonly ProductListFilter None = new();

    /// <summary>Busca no nome e na marca.</summary>
    public string? Search { get; init; }
    public string? Category { get; init; }
    public ProductCondition? Condition { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool FreeShippingOnly { get; init; }
    public ProductSort Sort { get; init; } = ProductSort.Newest;
}

/// <summary>Porta de persistência de produtos (EF Core na Infrastructure).</summary>
public interface IProductRepository
{
    Task AddAsync(Product product, CancellationToken cancellationToken);

    Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Página de produtos que passam no filtro, na ordem pedida, e o total que passa no filtro.</summary>
    Task<(IReadOnlyList<Product> Items, int TotalCount)> ListAsync(
        ProductListFilter filter, int page, int pageSize, CancellationToken cancellationToken);
}
