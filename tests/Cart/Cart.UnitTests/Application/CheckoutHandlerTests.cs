using BuildingBlocks.Application;
using BuildingBlocks.Application.Messaging;
using Cart.Application.Checkout;
using Cart.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Cart.UnitTests.Application;

internal sealed class FakeEventPublisher : IEventPublisher
{
    public List<(string EventType, string Key, Guid EventId, object Data)> Published { get; } = [];
    public bool Unavailable { get; set; }

    public Task PublishAsync<T>(string eventType, string key, Guid eventId, T data, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            throw new EventPublishingException("broker down");
        }
        Published.Add((eventType, key, eventId, data!));
        return Task.CompletedTask;
    }
}

public class CheckoutHandlerTests
{
    private static readonly Guid BuyerId = Guid.CreateVersion7();
    private static readonly Guid SellerId = Guid.CreateVersion7();
    private readonly InMemoryCartRepository _carts = new();
    private readonly FakeEventPublisher _events = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero));

    private CheckoutHandler Handler => new(_carts, _events, _clock);

    private ShoppingCart CartWith(params (string Name, decimal Price, int Quantity)[] items)
    {
        var cart = ShoppingCart.Create(BuyerId, _clock);
        foreach (var (name, price, quantity) in items)
        {
            cart.AddItem(Guid.CreateVersion7(), SellerId, name, Money.Create(price), quantity, _clock);
        }
        _carts.Carts[BuyerId] = cart;
        return cart;
    }

    private Task<Result<CheckoutResponse>> Checkout() => Handler.HandleAsync(new CheckoutCommand(BuyerId));

    [Fact]
    public async Task Publishes_the_cart_snapshot_and_empties_the_cart()
    {
        var cart = CartWith(("Furadeira", 199.90m, 2), ("Chaves", 89.50m, 1));
        var cartId = cart.Id;

        var result = await Checkout();

        var (eventType, key, eventId, data) = Assert.Single(_events.Published);
        var checkedOut = Assert.IsType<CartCheckedOut>(data);
        Assert.Equal(("cart.checked-out", cartId.ToString()), (eventType, key));
        Assert.Equal(result.Value.CheckoutId, eventId);
        Assert.Equal((result.Value.CheckoutId, cartId, BuyerId), (checkedOut.CheckoutId, checkedOut.CartId, checkedOut.BuyerId));
        Assert.Equal(["Furadeira", "Chaves"], checkedOut.Items.Select(i => i.ProductName));
        Assert.Equal((SellerId, 199.90m, "BRL", 2),
            (checkedOut.Items[0].SellerId, checkedOut.Items[0].UnitPrice, checkedOut.Items[0].Currency, checkedOut.Items[0].Quantity));
        Assert.Empty(_carts.Carts[BuyerId].Items);
    }

    [Fact]
    public async Task The_order_id_is_the_checkout_id()
    {
        CartWith(("Furadeira", 10m, 1));

        var result = await Checkout();

        Assert.Equal(result.Value.CheckoutId, result.Value.OrderId);
    }

    [Fact]
    public void Checkout_id_is_the_same_for_an_unchanged_cart_and_differs_after_a_change()
    {
        var cart = CartWith(("Furadeira", 10m, 1));

        var first = CheckoutIds.For(cart);
        var again = CheckoutIds.For(cart);
        _clock.Advance(TimeSpan.FromSeconds(1));
        cart.ChangeQuantity(cart.Items[0].ProductId, 2, _clock);

        Assert.Equal(first, again);
        Assert.NotEqual(first, CheckoutIds.For(cart));
    }

    [Fact]
    public async Task Empty_or_missing_cart_cannot_be_checked_out()
    {
        var missing = await Checkout();
        CartWith();
        var empty = await Checkout();

        Assert.Equal("cart.empty", Assert.Single(missing.Errors).Code);
        Assert.Equal("cart.empty", Assert.Single(empty.Errors).Code);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Broker_down_keeps_the_cart_and_reports_unavailable()
    {
        CartWith(("Furadeira", 10m, 1));
        _events.Unavailable = true;

        var error = Assert.Single((await Checkout()).Errors);

        Assert.Equal((ErrorType.Unavailable, "events.unavailable"), (error.Type, error.Code));
        Assert.Single(_carts.Carts[BuyerId].Items);
        Assert.Equal(0, _carts.Saves);
    }
}
