using Catalog.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.UnitTests;

public class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SellerId = Guid.CreateVersion7();
    private readonly FakeTimeProvider _clock = new(Now);

    private Product Create(
        Guid? sellerId = null,
        string name = "Furadeira de impacto",
        string? description = "700W, com maleta",
        decimal price = 199.90m,
        int stock = 5) =>
        Product.Create(sellerId ?? SellerId, name, description, Money.Create(price), stock, _clock);

    private static void AssertInvalid(string field, Action action) =>
        Assert.Equal(field, Assert.Throws<DomainException>(action).Field);

    [Fact]
    public void Create_sets_all_data()
    {
        var product = Create(name: "  Furadeira  ", description: "  700W  ", price: 199.90m, stock: 5);

        Assert.Equal(SellerId, product.SellerId);
        Assert.Equal("Furadeira", product.Name);
        Assert.Equal("700W", product.Description);
        Assert.Equal(Money.Create(199.90m), product.Price);
        Assert.Equal(5, product.Stock);
    }

    [Fact]
    public void Create_assigns_version_7_id_and_timestamps()
    {
        var product = Create();

        Assert.Equal(7, product.Id.Version);
        Assert.Equal(Now, product.CreatedAt);
        Assert.Equal(Now, product.UpdatedAt);
    }

    [Fact]
    public void Empty_seller_is_invalid()
    {
        AssertInvalid("sellerId", () => Create(sellerId: Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_is_invalid(string name)
    {
        AssertInvalid("name", () => Create(name: name));
    }

    [Fact]
    public void Name_longer_than_120_chars_is_invalid()
    {
        AssertInvalid("name", () => Create(name: new string('a', 121)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_description_becomes_null(string? description)
    {
        Assert.Null(Create(description: description).Description);
    }

    [Fact]
    public void Description_longer_than_2000_chars_is_invalid()
    {
        AssertInvalid("description", () => Create(description: new string('a', 2001)));
    }

    [Fact]
    public void Zero_price_is_invalid()
    {
        AssertInvalid("price", () => Create(price: 0m));
    }

    [Fact]
    public void Zero_stock_is_valid()
    {
        Assert.Equal(0, Create(stock: 0).Stock);
    }

    [Fact]
    public void Negative_stock_is_invalid()
    {
        AssertInvalid("stock", () => Create(stock: -1));
    }

    [Fact]
    public void Change_price_updates_price_and_timestamp()
    {
        var product = Create(price: 100m);
        _clock.Advance(TimeSpan.FromHours(1));

        product.ChangePrice(Money.Create(120m), _clock);

        Assert.Equal(Money.Create(120m), product.Price);
        Assert.Equal(Now.AddHours(1), product.UpdatedAt);
    }

    [Fact]
    public void Change_price_to_zero_is_invalid()
    {
        var product = Create();

        AssertInvalid("price", () => product.ChangePrice(Money.Create(0m), _clock));
    }

    [Fact]
    public void Add_stock_increases_quantity()
    {
        var product = Create(stock: 5);

        product.AddStock(3, _clock);

        Assert.Equal(8, product.Stock);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Add_stock_requires_positive_quantity(int quantity)
    {
        var product = Create();

        AssertInvalid("quantity", () => product.AddStock(quantity, _clock));
    }

    [Fact]
    public void Remove_stock_decreases_quantity_and_updates_timestamp()
    {
        var product = Create(stock: 5);
        _clock.Advance(TimeSpan.FromMinutes(5));

        product.RemoveStock(5, _clock);

        Assert.Equal(0, product.Stock);
        Assert.Equal(Now.AddMinutes(5), product.UpdatedAt);
    }

    [Fact]
    public void Removing_more_than_available_is_invalid_and_keeps_stock()
    {
        var product = Create(stock: 2);

        AssertInvalid("stock", () => product.RemoveStock(3, _clock));
        Assert.Equal(2, product.Stock);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Remove_stock_requires_positive_quantity(int quantity)
    {
        var product = Create();

        AssertInvalid("quantity", () => product.RemoveStock(quantity, _clock));
    }

    [Fact]
    public void Is_owned_by_its_seller_only()
    {
        var product = Create();

        Assert.True(product.IsOwnedBy(SellerId));
        Assert.False(product.IsOwnedBy(Guid.CreateVersion7()));
    }
}
