using BuildingBlocks.Application;
using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application.CreateProduct;

/// <summary><see cref="SellerId"/> vem do token de quem chama, nunca do corpo da requisição.</summary>
public sealed record CreateProductCommand(
    Guid SellerId, string? Name, string? Description, decimal? Price, string? Currency, int? Stock);

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

        Product product;
        try
        {
            var price = Money.Create(command.Price.Value, command.Currency ?? Money.DefaultCurrency);
            product = Product.Create(command.SellerId, command.Name!, command.Description, price, command.Stock ?? 0, clock);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Field, $"{ex.Field ?? "request"}.invalid", ex.Message);
        }

        await products.AddAsync(product, cancellationToken);
        return ProductResponse.From(product);
    }
}
