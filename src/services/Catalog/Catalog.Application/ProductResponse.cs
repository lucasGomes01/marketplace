using Catalog.Domain;

namespace Catalog.Application;

public sealed record ProductResponse(
    Guid Id,
    Guid SellerId,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    int Stock,
    DateTimeOffset CreatedAt)
{
    public static ProductResponse From(Product product) => new(
        product.Id,
        product.SellerId,
        product.Name,
        product.Description,
        product.Price.Amount,
        product.Price.Currency,
        product.Stock,
        product.CreatedAt);
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
