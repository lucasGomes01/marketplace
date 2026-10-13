using System.Security.Cryptography;
using BuildingBlocks.Application;
using BuildingBlocks.Application.Messaging;
using Cart.Application.Abstractions;
using Cart.Domain;

namespace Cart.Application.Checkout;

/// <summary>Evento <c>cart.checked-out</c> (contrato em <c>.ai/context/events.md</c>).</summary>
public sealed record CartCheckedOut(Guid CheckoutId, Guid CartId, Guid BuyerId, IReadOnlyList<CartCheckedOutItem> Items)
{
    public const string EventType = "cart.checked-out";
}

public sealed record CartCheckedOutItem(
    Guid ProductId, Guid SellerId, string ProductName, decimal UnitPrice, string Currency, int Quantity);

public sealed record CheckoutCommand(Guid BuyerId);

/// <summary><see cref="OrderId"/> = <see cref="CheckoutId"/>: o Orders cria o pedido com esse id.</summary>
public sealed record CheckoutResponse(Guid CheckoutId, Guid OrderId);

public static class CheckoutIds
{
    /// <summary>
    /// Id determinístico do checkout: o mesmo carrinho (mesmo id e mesma última alteração) gera sempre o mesmo id.
    /// Se o checkout for repetido (ex.: falhou depois de publicar), o Orders recebe o mesmo id e não duplica o pedido.
    /// </summary>
    public static Guid For(ShoppingCart cart)
    {
        Span<byte> input = stackalloc byte[24];
        cart.Id.TryWriteBytes(input);
        BitConverter.TryWriteBytes(input[16..], cart.UpdatedAt.UtcTicks);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80); // versão 8 (UUID "custom", RFC 9562)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variante RFC 4122
        return new Guid(bytes);
    }
}

/// <summary>Publica o snapshot do carrinho e depois o esvazia (os itens passam a viver no pedido).</summary>
public sealed class CheckoutHandler(ICartRepository carts, IEventPublisher events, TimeProvider clock)
    : IHandler<CheckoutCommand, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> HandleAsync(CheckoutCommand command, CancellationToken cancellationToken = default)
    {
        var cart = await carts.FindByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null || cart.Items.Count == 0)
        {
            return Error.Validation("cart", "cart.empty", "The cart is empty.");
        }

        var checkoutId = CheckoutIds.For(cart);
        var checkedOut = new CartCheckedOut(checkoutId, cart.Id, cart.BuyerId, cart.Items
            .Select(i => new CartCheckedOutItem(i.ProductId, i.SellerId, i.ProductName, i.UnitPrice.Amount, i.UnitPrice.Currency, i.Quantity))
            .ToList());

        // Publica primeiro: se o broker estiver fora, nada muda e a pessoa pode tentar de novo.
        try
        {
            await events.PublishAsync(CartCheckedOut.EventType, cart.Id.ToString(), checkoutId, checkedOut, cancellationToken);
        }
        catch (EventPublishingException)
        {
            return Error.Unavailable("events.unavailable", "Could not place the order right now. Try again in a moment.");
        }

        cart.Clear(clock);
        var saved = await CartErrors.SaveAsync(carts, cart, cancellationToken);
        return saved.IsSuccess ? new CheckoutResponse(checkoutId, checkoutId) : Result<CheckoutResponse>.Failure(saved.Errors);
    }
}
