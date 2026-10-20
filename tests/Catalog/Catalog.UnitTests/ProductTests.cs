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

    private Product CreateWith(ProductDetails details, decimal price = 199.90m) =>
        Product.Create(SellerId, "Fone", null, Money.Create(price), 1, _clock, details);

    [Fact]
    public void Without_details_the_product_is_new_in_the_default_category_with_no_images()
    {
        var product = Create();

        Assert.Equal((ProductCategories.Default, ProductCondition.New, false), (product.Category, product.Condition, product.FreeShipping));
        Assert.Empty(product.ImageUrls);
        Assert.Empty(product.Attributes);
        Assert.Null(product.OriginalPrice);
    }

    [Fact]
    public void Create_keeps_the_listing_details_normalized()
    {
        var product = CreateWith(new ProductDetails
        {
            Category = "  Eletronicos ",
            Brand = "  Sony ",
            Sku = " WH-1000 ",
            Condition = ProductCondition.Used,
            ImageUrls = [" https://cdn.example.com/a.webp ", "", "http://cdn.example.com/b.png"],
            OriginalPrice = 249.90m,
            WeightGrams = 250,
            LengthCm = 20,
            WidthCm = 18,
            HeightCm = 8,
            WarrantyMonths = 3,
            FreeShipping = true,
            Attributes = [new(" Cor ", " Preto "), new("", " ")],
        });

        Assert.Equal(("eletronicos", "Sony", "WH-1000", ProductCondition.Used), (product.Category, product.Brand, product.Sku, product.Condition));
        Assert.Equal(["https://cdn.example.com/a.webp", "http://cdn.example.com/b.png"], product.ImageUrls);
        Assert.Equal((249.90m, 250, 20, 18, 8, 3, true),
            (product.OriginalPrice, product.WeightGrams, product.LengthCm, product.WidthCm, product.HeightCm, product.WarrantyMonths, product.FreeShipping));
        Assert.Equal(new ProductAttribute("Cor", "Preto"), Assert.Single(product.Attributes));
    }

    [Fact]
    public void Unknown_category_is_invalid() =>
        AssertInvalid("category", () => CreateWith(new ProductDetails { Category = "armas" }));

    [Fact]
    public void Unknown_condition_is_invalid() =>
        AssertInvalid("condition", () => CreateWith(new ProductDetails { Condition = (ProductCondition)42 }));

    [Theory]
    [InlineData("ftp://cdn.example.com/a.png")]
    [InlineData("/imagens/a.png")]
    [InlineData("javascript:alert(1)")]
    public void Image_must_be_an_absolute_http_url(string url) =>
        AssertInvalid("imageUrls", () => CreateWith(new ProductDetails { ImageUrls = [url] }));

    [Fact]
    public void More_than_8_images_is_invalid() =>
        AssertInvalid("imageUrls", () => CreateWith(new ProductDetails
        {
            ImageUrls = Enumerable.Range(1, 9).Select(i => $"https://cdn.example.com/{i}.webp").ToList(),
        }));

    [Theory]
    [InlineData(199.90)]
    [InlineData(100)]
    [InlineData(250.001)]
    public void Original_price_must_be_above_the_price_with_2_decimals(double originalPrice) =>
        AssertInvalid("originalPrice", () => CreateWith(new ProductDetails { OriginalPrice = (decimal)originalPrice }, price: 199.90m));

    [Theory]
    [InlineData("weightGrams")]
    [InlineData("lengthCm")]
    [InlineData("warrantyMonths")]
    public void Out_of_range_measures_are_invalid(string field)
    {
        var details = field switch
        {
            "weightGrams" => new ProductDetails { WeightGrams = 0 },
            "lengthCm" => new ProductDetails { LengthCm = 1001 },
            _ => new ProductDetails { WarrantyMonths = -1 },
        };

        AssertInvalid(field, () => CreateWith(details));
    }

    [Fact]
    public void Attribute_needs_name_and_value() =>
        AssertInvalid("attributes", () => CreateWith(new ProductDetails { Attributes = [new("Cor", "")] }));

    [Fact]
    public void Brand_longer_than_60_chars_is_invalid() =>
        AssertInvalid("brand", () => CreateWith(new ProductDetails { Brand = new string('a', 61) }));

    [Fact]
    public void Raising_the_price_to_the_original_price_drops_the_discount()
    {
        var product = CreateWith(new ProductDetails { OriginalPrice = 249.90m });

        product.ChangePrice(Money.Create(249.90m), _clock);

        Assert.Null(product.OriginalPrice);
    }

    [Fact]
    public void Is_owned_by_its_seller_only()
    {
        var product = Create();

        Assert.True(product.IsOwnedBy(SellerId));
        Assert.False(product.IsOwnedBy(Guid.CreateVersion7()));
    }
}
