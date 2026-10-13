using BuildingBlocks.Application;
using Cart.Application;
using Cart.Application.AddItem;
using Cart.Application.ChangeQuantity;
using Cart.Application.GetCart;
using Cart.Application.RemoveItem;
using Microsoft.Extensions.Time.Testing;

namespace Cart.UnitTests.Application;

public class CartHandlersTests
{
    private static readonly Guid BuyerId = Guid.CreateVersion7();
    private readonly InMemoryCartRepository _carts = new();
    private readonly FakeProductCatalog _catalog = new();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private Task<Result<CartResponse>> Add(Guid? productId, int? quantity = 1, Guid? buyerId = null) =>
        new AddItemHandler(_carts, _catalog, _clock).HandleAsync(new AddCartItemCommand(buyerId ?? BuyerId, productId, quantity));

    private Task<Result<CartResponse>> Change(Guid productId, int? quantity) =>
        new ChangeQuantityHandler(_carts, _catalog, _clock).HandleAsync(new ChangeCartItemQuantityCommand(BuyerId, productId, quantity));

    private Task<Result<CartResponse>> Remove(Guid productId) =>
        new RemoveItemHandler(_carts, _clock).HandleAsync(new RemoveCartItemCommand(BuyerId, productId));

    private static Error SingleError<T>(Result<T> result) => Assert.Single(result.Errors);

    [Fact]
    public async Task A_buyer_without_cart_gets_an_empty_one()
    {
        var result = await new GetCartHandler(_carts).HandleAsync(new GetCartQuery(BuyerId));

        Assert.Empty(result.Value.Items);
        Assert.Equal((0, 0m, "BRL"), (result.Value.TotalQuantity, result.Value.Total, result.Value.Currency));
        Assert.Empty(_carts.Carts);
    }

    [Fact]
    public async Task Adding_uses_name_price_and_seller_from_the_catalog()
    {
        var drill = _catalog.Add("Furadeira", 199.90m, stock: 5);

        var result = await Add(drill.Id, 2);

        var item = Assert.Single(result.Value.Items);
        Assert.Equal((drill.Id, drill.SellerId, "Furadeira", 199.90m, 2, 399.80m),
            (item.ProductId, item.SellerId, item.ProductName, item.UnitPrice, item.Quantity, item.Subtotal));
        Assert.Equal(399.80m, result.Value.Total);
        Assert.Equal(1, _carts.Saves);
    }

    [Fact]
    public async Task Adding_again_sums_and_uses_the_current_price()
    {
        var drill = _catalog.Add(price: 199.90m, stock: 5);
        await Add(drill.Id, 1);
        _catalog.Products[drill.Id] = drill with { Price = 179.90m };

        var result = await Add(drill.Id, 2);

        var item = Assert.Single(result.Value.Items);
        Assert.Equal((3, 179.90m), (item.Quantity, item.UnitPrice));
    }

    [Fact]
    public async Task Missing_quantity_means_one()
    {
        var drill = _catalog.Add();

        var result = await Add(drill.Id, quantity: null);

        Assert.Equal(1, result.Value.TotalQuantity);
    }

    [Fact]
    public async Task Missing_product_id_is_a_validation_error()
    {
        var error = SingleError(await Add(productId: null));

        Assert.Equal((ErrorType.Validation, "productId"), (error.Type, error.Field));
    }

    [Fact]
    public async Task Unknown_product_is_not_found()
    {
        var error = SingleError(await Add(Guid.CreateVersion7()));

        Assert.Equal((ErrorType.NotFound, "product.not_found"), (error.Type, error.Code));
        Assert.Empty(_carts.Carts);
    }

    [Fact]
    public async Task Cannot_buy_your_own_product()
    {
        var mine = _catalog.Add(sellerId: BuyerId);

        var error = SingleError(await Add(mine.Id));

        Assert.Equal((ErrorType.Validation, "productId", "cart.own_product"), (error.Type, error.Field, error.Code));
    }

    [Fact]
    public async Task Sold_out_product_cannot_be_added()
    {
        var soldOut = _catalog.Add(stock: 0);

        var error = SingleError(await Add(soldOut.Id));

        Assert.Equal(("quantity", "cart.out_of_stock"), (error.Field, error.Code));
    }

    [Fact]
    public async Task Total_in_the_cart_cannot_exceed_the_stock()
    {
        var drill = _catalog.Add(stock: 3);
        await Add(drill.Id, 2);

        var error = SingleError(await Add(drill.Id, 2));

        Assert.Equal(("quantity", "cart.insufficient_stock"), (error.Field, error.Code));
        Assert.Contains("3", error.Message);
        Assert.Equal(2, _carts.Carts[BuyerId].QuantityOf(drill.Id));
    }

    [Fact]
    public async Task Domain_rules_become_validation_errors()
    {
        var drill = _catalog.Add(stock: 500);

        var error = SingleError(await Add(drill.Id, 100));

        Assert.Equal((ErrorType.Validation, "quantity"), (error.Type, error.Field));
    }

    [Fact]
    public async Task Catalog_down_is_reported_as_unavailable()
    {
        _catalog.Unavailable = true;

        var error = SingleError(await Add(Guid.CreateVersion7()));

        Assert.Equal((ErrorType.Unavailable, "catalog.unavailable"), (error.Type, error.Code));
    }

    [Fact]
    public async Task Concurrent_update_is_a_conflict()
    {
        var drill = _catalog.Add();
        _carts.SimulateConcurrentUpdate = true;

        var error = SingleError(await Add(drill.Id));

        Assert.Equal((ErrorType.Conflict, "cart.concurrent_update"), (error.Type, error.Code));
    }

    [Fact]
    public async Task Change_quantity_checks_the_current_stock()
    {
        var drill = _catalog.Add(stock: 5);
        await Add(drill.Id, 1);

        var ok = await Change(drill.Id, 5);
        var tooMany = await Change(drill.Id, 6);

        Assert.Equal(5, ok.Value.TotalQuantity);
        Assert.Equal("cart.insufficient_stock", SingleError(tooMany).Code);
    }

    [Fact]
    public async Task Change_or_remove_an_item_not_in_the_cart_is_not_found()
    {
        var drill = _catalog.Add();

        Assert.Equal("cart.item_not_found", SingleError(await Change(drill.Id, 1)).Code);
        Assert.Equal("cart.item_not_found", SingleError(await Remove(drill.Id)).Code);
    }

    [Fact]
    public async Task Change_quantity_requires_a_quantity()
    {
        var drill = _catalog.Add();
        await Add(drill.Id);

        Assert.Equal("quantity", SingleError(await Change(drill.Id, null)).Field);
    }

    [Fact]
    public async Task Remove_takes_the_item_out()
    {
        var drill = _catalog.Add();
        var wrench = _catalog.Add("Chaves", 10m);
        await Add(drill.Id);
        await Add(wrench.Id);

        var result = await Remove(drill.Id);

        Assert.Equal([wrench.Id], result.Value.Items.Select(i => i.ProductId));
    }

    [Fact]
    public async Task Remove_does_not_need_the_catalog()
    {
        var drill = _catalog.Add();
        await Add(drill.Id);
        _catalog.Unavailable = true;

        var result = await Remove(drill.Id);

        Assert.True(result.IsSuccess);
    }
}
