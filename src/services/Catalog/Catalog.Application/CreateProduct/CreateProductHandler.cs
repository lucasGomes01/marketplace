using BuildingBlocks.Application;
using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application.CreateProduct;

/// <summary>Uma linha da ficha técnica como chega na requisição.</summary>
public sealed record ProductAttributeInput(string? Name, string? Value);

/// <summary><see cref="SellerId"/> vem do token de quem chama, nunca do corpo da requisição.</summary>
public sealed record CreateProductCommand(
    Guid SellerId, string? Name, string? Description, decimal? Price, string? Currency, int? Stock)
{
    public string? Category { get; init; }
    public string? Brand { get; init; }
    public string? Sku { get; init; }
    public string? Condition { get; init; }
    public IReadOnlyList<string>? ImageUrls { get; init; }
    public decimal? OriginalPrice { get; init; }
    public int? WeightGrams { get; init; }
    public int? LengthCm { get; init; }
    public int? WidthCm { get; init; }
    public int? HeightCm { get; init; }
    public int? WarrantyMonths { get; init; }
    public bool? FreeShipping { get; init; }
    public IReadOnlyList<ProductAttributeInput>? Attributes { get; init; }
}

public sealed class CreateProductHandler(IProductRepository products, TimeProvider clock)
    : IHandler<CreateProductCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(
        CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Price is null)
        {
            return Error.Validation("price", "price.required", "Price is required.");
        }
        if (!ProductConditions.TryParse(command.Condition, out var condition))
        {
            return Error.Validation("condition", "condition.invalid", "Condition must be New, Used or Refurbished.");
        }

        Product product;
        try
        {
            var price = Money.Create(command.Price.Value, command.Currency ?? Money.DefaultCurrency);
            var details = new ProductDetails
            {
                Category = command.Category,
                Brand = command.Brand,
                Sku = command.Sku,
                Condition = condition,
                ImageUrls = command.ImageUrls,
                OriginalPrice = command.OriginalPrice,
                WeightGrams = command.WeightGrams,
                LengthCm = command.LengthCm,
                WidthCm = command.WidthCm,
                HeightCm = command.HeightCm,
                WarrantyMonths = command.WarrantyMonths,
                FreeShipping = command.FreeShipping ?? false,
                Attributes = command.Attributes?.Select(a => new ProductAttribute(a.Name ?? "", a.Value ?? "")).ToList(),
            };
            product = Product.Create(command.SellerId, command.Name!, command.Description, price, command.Stock ?? 0, clock, details);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Field, $"{ex.Field ?? "request"}.invalid", ex.Message);
        }

        await products.AddAsync(product, cancellationToken);
        return ProductResponse.From(product);
    }
}
