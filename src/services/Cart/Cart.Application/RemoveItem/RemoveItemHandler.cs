using BuildingBlocks.Application;
using Cart.Application.Abstractions;

namespace Cart.Application.RemoveItem;

public sealed record RemoveCartItemCommand(Guid BuyerId, Guid ProductId);

/// <summary>Remover não depende do Catalog: funciona mesmo com ele fora do ar.</summary>
public sealed class RemoveItemHandler(ICartRepository carts, TimeProvider clock) : IHandler<RemoveCartItemCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(RemoveCartItemCommand command, CancellationToken cancellationToken = default)
    {
        var cart = await carts.FindByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null || cart.QuantityOf(command.ProductId) == 0)
        {
            return CartErrors.ItemNotFound;
        }

        cart.RemoveItem(command.ProductId, clock);
        return await CartErrors.SaveAsync(carts, cart, cancellationToken);
    }
}
