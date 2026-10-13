using BuildingBlocks.Application;
using Cart.Application.Abstractions;
using Cart.Domain;

namespace Cart.Application.ChangeQuantity;

public sealed record ChangeCartItemQuantityCommand(Guid BuyerId, Guid ProductId, int? Quantity);

public sealed class ChangeQuantityHandler(ICartRepository carts, IProductCatalog catalog, TimeProvider clock)
    : IHandler<ChangeCartItemQuantityCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(
        ChangeCartItemQuantityCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Quantity is not { } quantity)
        {
            return Error.Validation("quantity", "quantity.required", "Quantity is required.");
        }

        var cart = await carts.FindByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null || cart.QuantityOf(command.ProductId) == 0)
        {
            return CartErrors.ItemNotFound;
        }

        CatalogProduct? product;
        try
        {
            product = await catalog.FindAsync(command.ProductId, cancellationToken);
        }
        catch (CatalogUnavailableException)
        {
            return CartErrors.CatalogUnavailable;
        }
        if (product is null)
        {
            return CartErrors.ProductNotFound;
        }
        if (CartErrors.CheckStock(product, quantity) is { } stockError)
        {
            return stockError;
        }

        try
        {
            cart.ChangeQuantity(command.ProductId, quantity, clock);
        }
        catch (DomainException ex)
        {
            return CartErrors.FromDomain(ex);
        }

        return await CartErrors.SaveAsync(carts, cart, cancellationToken);
    }
}
