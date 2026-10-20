using BuildingBlocks.Application;
using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application.ListProducts;

public sealed record ListProductsQuery(int? Page, int? PageSize)
{
    public string? Search { get; init; }
    public string? Category { get; init; }
    public string? Condition { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool? FreeShipping { get; init; }

    /// <summary><c>newest</c> (padrão), <c>price_asc</c>, <c>price_desc</c> ou <c>name</c>.</summary>
    public string? Sort { get; init; }
}

public sealed class ListProductsHandler(IProductRepository products)
    : IHandler<ListProductsQuery, PagedResponse<ProductResponse>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int SearchMaxLength = 100;

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

        var filter = BuildFilter(query);
        if (!filter.IsSuccess)
        {
            return Result<PagedResponse<ProductResponse>>.Failure(filter.Errors);
        }

        var (items, totalCount) = await products.ListAsync(filter.Value, page, pageSize, cancellationToken);
        return new PagedResponse<ProductResponse>(items.Select(ProductResponse.From).ToList(), page, pageSize, totalCount);
    }

    private static Result<ProductListFilter> BuildFilter(ListProductsQuery query)
    {
        var search = query.Search?.Trim();
        if (search?.Length > SearchMaxLength)
        {
            return Error.Validation("q", "q.invalid", $"Search must have at most {SearchMaxLength} characters.");
        }

        string? category = null;
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            category = query.Category.Trim().ToLowerInvariant();
            if (!ProductCategories.All.Contains(category))
            {
                return Error.Validation("category", "category.invalid", "Unknown category.");
            }
        }

        ProductCondition? condition = null;
        if (!string.IsNullOrWhiteSpace(query.Condition))
        {
            if (!ProductConditions.TryParse(query.Condition, out var parsed))
            {
                return Error.Validation("condition", "condition.invalid", "Condition must be New, Used or Refurbished.");
            }
            condition = parsed;
        }

        if (query.MinPrice < 0 || query.MaxPrice < 0)
        {
            return Error.Validation("minPrice", "price_range.invalid", "Price range cannot be negative.");
        }
        if (query.MinPrice > query.MaxPrice)
        {
            return Error.Validation("minPrice", "price_range.invalid", "Minimum price cannot be greater than maximum price.");
        }

        ProductSort? sort = (query.Sort?.Trim().ToLowerInvariant() ?? "newest") switch
        {
            "" or "newest" => ProductSort.Newest,
            "price_asc" => ProductSort.PriceAscending,
            "price_desc" => ProductSort.PriceDescending,
            "name" => ProductSort.Name,
            _ => null,
        };
        if (sort is null)
        {
            return Error.Validation("sort", "sort.invalid", "Sort must be newest, price_asc, price_desc or name.");
        }

        return new ProductListFilter
        {
            Search = string.IsNullOrEmpty(search) ? null : search,
            Category = category,
            Condition = condition,
            MinPrice = query.MinPrice,
            MaxPrice = query.MaxPrice,
            FreeShippingOnly = query.FreeShipping ?? false,
            Sort = sort.Value,
        };
    }
}
