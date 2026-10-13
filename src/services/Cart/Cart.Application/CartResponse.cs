using BuildingBlocks.Application;
using Cart.Application.Abstractions;
using Cart.Domain;

namespace Cart.Application;

public sealed record CartItemResponse(
    Guid ProductId, Guid SellerId, string ProductName, decimal UnitPrice, string Currency, int Quantity, decimal Subtotal);

public sealed record CartResponse(IReadOnlyList<CartItemResponse> Items, int TotalQuantity, decimal Total, string Currency)
{
    public static CartResponse Empty { get; } = new([], 0, 0m, Money.DefaultCurrency);

    public static CartResponse From(ShoppingCart cart) => new(
        cart.Items.Select(i => new CartItemResponse(
            i.ProductId, i.SellerId, i.ProductName, i.UnitPrice.Amount, i.UnitPrice.Currency, i.Quantity, i.Subtotal.Amount)).ToList(),
        cart.TotalQuantity,
        cart.Total.Amount,
        cart.Currency);
}

/// <summary>Erros e regras compartilhados pelos casos de uso do carrinho.</summary>
internal static class CartErrors
{
    public static readonly Error ItemNotFound = Error.NotFound("cart.item_not_found", "This product is not in the cart.");
    public static readonly Error ProductNotFound = Error.NotFound("product.not_found", "Product not found.");
    public static readonly Error CatalogUnavailable =
        Error.Unavailable("catalog.unavailable", "The catalog is unavailable. Try again in a moment.");
    public static readonly Error ConcurrentUpdate =
        Error.Conflict("cart.concurrent_update", "The cart was changed at the same time. Reload it and try again.");

    public static Error FromDomain(DomainException ex) =>
        Error.Validation(ex.Field, $"{ex.Field ?? "request"}.invalid", ex.Message);

    /// <summary>A quantidade total desejada cabe no estoque atual?</summary>
    public static Error? CheckStock(CatalogProduct product, int desiredQuantity) =>
        product.Stock <= 0
            ? Error.Validation("quantity", "cart.out_of_stock", "This product is out of stock.")
            : desiredQuantity > product.Stock
                ? Error.Validation("quantity", "cart.insufficient_stock", $"Only {product.Stock} unit(s) in stock.")
                : null;

    /// <summary>Grava traduzindo a corrida de concorrência em erro esperado.</summary>
    public static async Task<Result<CartResponse>> SaveAsync(
        ICartRepository carts, ShoppingCart cart, CancellationToken cancellationToken)
    {
        try
        {
            await carts.SaveAsync(cart, cancellationToken);
        }
        catch (CartConcurrencyException)
        {
            return ConcurrentUpdate;
        }
        return CartResponse.From(cart);
    }
}
