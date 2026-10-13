using Cart.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Cart.UnitTests;

public class ShoppingCartTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid BuyerId = Guid.CreateVersion7();
    private static readonly Guid SellerA = Guid.CreateVersion7();
    private static readonly Guid SellerB = Guid.CreateVersion7();
    private static readonly Guid Drill = Guid.CreateVersion7();
    private static readonly Guid Wrench = Guid.CreateVersion7();
    private readonly FakeTimeProvider _clock = new(Now);

    private ShoppingCart NewCart() => ShoppingCart.Create(BuyerId, _clock);

    private static void AssertInvalid(string field, Action action) =>
        Assert.Equal(field, Assert.Throws<DomainException>(action).Field);

    [Fact]
    public void A_new_cart_is_empty()
    {
        var cart = NewCart();

        Assert.Equal(BuyerId, cart.BuyerId);
        Assert.Empty(cart.Items);
        Assert.Equal(0, cart.TotalQuantity);
        Assert.Equal(Money.Create(0m), cart.Total);
        Assert.Equal(Now, cart.CreatedAt);
    }

    [Fact]
    public void Empty_buyer_is_invalid()
    {
        AssertInvalid("buyerId", () => ShoppingCart.Create(Guid.Empty, _clock));
    }

    [Fact]
    public void Adding_items_from_different_sellers_keeps_a_snapshot_of_each()
    {
        var cart = NewCart();

        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(199.90m), 1, _clock);
        cart.AddItem(Wrench, SellerB, "Chaves", Money.Create(89.50m), 2, _clock);

        Assert.Equal(2, cart.Items.Count);
        var wrench = cart.Items.Single(i => i.ProductId == Wrench);
        Assert.Equal((SellerB, "Chaves", Money.Create(89.50m), 2), (wrench.SellerId, wrench.ProductName, wrench.UnitPrice, wrench.Quantity));
        Assert.Equal(Money.Create(378.90m), cart.Total);
        Assert.Equal(3, cart.TotalQuantity);
    }

    [Fact]
    public void Adding_the_same_product_again_sums_quantity_and_refreshes_the_snapshot()
    {
        var cart = NewCart();
        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(199.90m), 1, _clock);

        cart.AddItem(Drill, SellerA, "Furadeira 700W", Money.Create(179.90m), 2, _clock);

        var item = Assert.Single(cart.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal("Furadeira 700W", item.ProductName);
        Assert.Equal(Money.Create(179.90m), item.UnitPrice);
        Assert.Equal(Money.Create(539.70m), item.Subtotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Quantity_must_be_between_1_and_99(int quantity)
    {
        var cart = NewCart();

        AssertInvalid("quantity", () => cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m), quantity, _clock));
        Assert.Empty(cart.Items);
    }

    [Fact]
    public void Summing_beyond_99_is_invalid_and_keeps_the_previous_quantity()
    {
        var cart = NewCart();
        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m), 98, _clock);

        AssertInvalid("quantity", () => cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m), 2, _clock));
        Assert.Equal(98, cart.QuantityOf(Drill));
    }

    [Fact]
    public void A_cart_has_a_single_currency()
    {
        var cart = NewCart();
        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m, "BRL"), 1, _clock);

        AssertInvalid("currency", () => cart.AddItem(Wrench, SellerB, "Chaves", Money.Create(10m, "USD"), 1, _clock));
        Assert.Single(cart.Items);
    }

    [Fact]
    public void Item_needs_a_product_a_seller_and_a_name()
    {
        var cart = NewCart();

        AssertInvalid("productId", () => cart.AddItem(Guid.Empty, SellerA, "X", Money.Create(1m), 1, _clock));
        AssertInvalid("sellerId", () => cart.AddItem(Drill, Guid.Empty, "X", Money.Create(1m), 1, _clock));
        AssertInvalid("productName", () => cart.AddItem(Drill, SellerA, " ", Money.Create(1m), 1, _clock));
    }

    [Fact]
    public void Change_quantity_sets_the_new_value()
    {
        var cart = NewCart();
        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m), 5, _clock);

        cart.ChangeQuantity(Drill, 2, _clock);

        Assert.Equal(2, cart.QuantityOf(Drill));
    }

    [Fact]
    public void Change_quantity_validates_range_and_item()
    {
        var cart = NewCart();
        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m), 5, _clock);

        AssertInvalid("quantity", () => cart.ChangeQuantity(Drill, 0, _clock));
        AssertInvalid("productId", () => cart.ChangeQuantity(Wrench, 1, _clock));
        Assert.Equal(5, cart.QuantityOf(Drill));
    }

    [Fact]
    public void Items_keep_the_order_they_were_added_even_after_removals()
    {
        var cart = NewCart();
        var third = Guid.CreateVersion7();
        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m), 1, _clock);
        cart.AddItem(Wrench, SellerB, "Chaves", Money.Create(5m), 1, _clock);
        cart.RemoveItem(Drill, _clock);
        cart.AddItem(third, SellerB, "Trena", Money.Create(5m), 1, _clock);
        cart.AddItem(Wrench, SellerB, "Chaves", Money.Create(5m), 1, _clock); // somar não muda a posição

        Assert.Equal(["Chaves", "Trena"], cart.Items.Select(i => i.ProductName));
        Assert.True(cart.Items[0].Position < cart.Items[1].Position);
    }

    [Fact]
    public void Remove_item_takes_it_out_of_the_cart()
    {
        var cart = NewCart();
        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m), 1, _clock);
        cart.AddItem(Wrench, SellerB, "Chaves", Money.Create(5m), 1, _clock);

        cart.RemoveItem(Drill, _clock);

        Assert.Equal([Wrench], cart.Items.Select(i => i.ProductId));
        Assert.Equal(0, cart.QuantityOf(Drill));
    }

    [Fact]
    public void Removing_an_item_that_is_not_in_the_cart_is_invalid()
    {
        AssertInvalid("productId", () => NewCart().RemoveItem(Drill, _clock));
    }

    [Fact]
    public void Clear_empties_the_cart_and_frees_the_currency()
    {
        var cart = NewCart();
        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m, "USD"), 1, _clock);

        cart.Clear(_clock);
        cart.AddItem(Wrench, SellerB, "Chaves", Money.Create(5m, "BRL"), 1, _clock);

        Assert.Equal(Money.Create(5m, "BRL"), cart.Total);
    }

    [Fact]
    public void Every_change_updates_the_timestamp()
    {
        var cart = NewCart();
        _clock.Advance(TimeSpan.FromMinutes(5));

        cart.AddItem(Drill, SellerA, "Furadeira", Money.Create(10m), 1, _clock);

        Assert.Equal(Now.AddMinutes(5), cart.UpdatedAt);
        Assert.Equal(Now, cart.CreatedAt);
    }
}
