using BuildingBlocks.Application;
using Cart.Application.Abstractions;
using Cart.Domain;

namespace Cart.Application.AddItem;

/// <summary>Só o id e a quantidade vêm de quem chama; nome, preço, vendedor e estoque vêm do Catalog.</summary>
public sealed record AddCartItemCommand(Guid BuyerId, Guid? ProductId, int? Quantity);

public sealed class AddItemHandler(ICartRepository carts, IProductCatalog catalog, TimeProvider clock)
    : IHandler<AddCartItemCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(AddCartItemCommand command, CancellationToken cancellationToken = default)
    {
        if (command.ProductId is not { } productId || productId == Guid.Empty)
        {
            return Error.Validation("productId", "productId.required", "Product is required.");
        }
        var quantity = command.Quantity ?? 1;

        CatalogProduct? product;
        try
        {
            product = await catalog.FindAsync(productId, cancellationToken);
        }
        catch (CatalogUnavailableException)
        {
            return CartErrors.CatalogUnavailable;
        }
        if (product is null)
        {
            return CartErrors.ProductNotFound;
        }
        if (product.SellerId == command.BuyerId)
        {
            return Error.Validation("productId", "cart.own_product", "You cannot buy your own product.");
        }

        var cart = await carts.FindByBuyerAsync(command.BuyerId, cancellationToken)
            ?? ShoppingCart.Create(command.BuyerId, clock);

        if (CartErrors.CheckStock(product, cart.QuantityOf(productId) + quantity) is { } stockError)
        {
            return stockError;
        }

        try
        {
            cart.AddItem(product.Id, product.SellerId, product.Name, Money.Create(product.Price, product.Currency), quantity, clock);
        }
        catch (DomainException ex)
        {
            return CartErrors.FromDomain(ex);
        }

        return await CartErrors.SaveAsync(carts, cart, cancellationToken);
    }
}
