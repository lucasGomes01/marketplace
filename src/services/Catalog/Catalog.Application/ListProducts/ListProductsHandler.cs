using BuildingBlocks.Application;
using Catalog.Application.Abstractions;

namespace Catalog.Application.ListProducts;

public sealed record ListProductsQuery(int? Page, int? PageSize);

public sealed class ListProductsHandler(IProductRepository products)
    : IHandler<ListProductsQuery, PagedResponse<ProductResponse>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public async Task<Result<PagedResponse<ProductResponse>>> HandleAsync(
        ListProductsQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? DefaultPageSize;

        if (page < 1)
        {
            return Error.Validation("page", "page.invalid", "Page must be 1 or greater.");
        }
        if (pageSize is < 1 or > MaxPageSize)
        {
            return Error.Validation("pageSize", "pageSize.invalid", $"Page size must be between 1 and {MaxPageSize}.");
        }

        var (items, totalCount) = await products.ListNewestFirstAsync(page, pageSize, cancellationToken);
        return new PagedResponse<ProductResponse>(items.Select(ProductResponse.From).ToList(), page, pageSize, totalCount);
    }
}
