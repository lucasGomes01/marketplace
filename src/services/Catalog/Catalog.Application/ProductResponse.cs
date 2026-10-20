using Catalog.Domain;

namespace Catalog.Application;

public sealed record ProductAttributeResponse(string Name, string Value);

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
    public string Category { get; init; } = ProductCategories.Default;
    public string? Brand { get; init; }
    public string? Sku { get; init; }
    public string Condition { get; init; } = nameof(ProductCondition.New);
    public IReadOnlyList<string> ImageUrls { get; init; } = [];
    public decimal? OriginalPrice { get; init; }
    public int? WeightGrams { get; init; }
    public int? LengthCm { get; init; }
    public int? WidthCm { get; init; }
    public int? HeightCm { get; init; }
    public int? WarrantyMonths { get; init; }
    public bool FreeShipping { get; init; }
    public IReadOnlyList<ProductAttributeResponse> Attributes { get; init; } = [];

    public static ProductResponse From(Product product) => new(
        product.Id,
        product.SellerId,
        product.Name,
        product.Description,
        product.Price.Amount,
        product.Price.Currency,
        product.Stock,
        product.CreatedAt)
    {
        Category = product.Category,
        Brand = product.Brand,
        Sku = product.Sku,
        Condition = product.Condition.ToString(),
        ImageUrls = product.ImageUrls.ToList(),
        OriginalPrice = product.OriginalPrice,
        WeightGrams = product.WeightGrams,
        LengthCm = product.LengthCm,
        WidthCm = product.WidthCm,
        HeightCm = product.HeightCm,
        WarrantyMonths = product.WarrantyMonths,
        FreeShipping = product.FreeShipping,
        Attributes = product.Attributes.Select(a => new ProductAttributeResponse(a.Name, a.Value)).ToList(),
    };
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
