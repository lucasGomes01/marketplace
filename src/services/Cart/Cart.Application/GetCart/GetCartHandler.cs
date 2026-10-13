using BuildingBlocks.Application;
using Cart.Application.Abstractions;

namespace Cart.Application.GetCart;

public sealed record GetCartQuery(Guid BuyerId);

public sealed class GetCartHandler(ICartRepository carts) : IHandler<GetCartQuery, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(GetCartQuery query, CancellationToken cancellationToken = default)
    {
        // Sem carrinho ainda: devolve um vazio sem gravar nada.
        var cart = await carts.FindByBuyerAsync(query.BuyerId, cancellationToken);
        return cart is null ? CartResponse.Empty : CartResponse.From(cart);
    }
}
