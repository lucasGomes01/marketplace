using BuildingBlocks.Application;
using Catalog.Application.CreateProduct;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.UnitTests.Application;

public class CreateProductHandlerTests
{
    private static readonly Guid SellerId = Guid.CreateVersion7();
    private readonly InMemoryProductRepository _repository = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly CreateProductHandler _handler;

    public CreateProductHandlerTests() => _handler = new CreateProductHandler(_repository, _clock);

    private Task<Result<Catalog.Application.ProductResponse>> Create(
        string? name = "Furadeira", string? description = "700W", decimal? price = 199.90m, string? currency = null, int? stock = 5) =>
        _handler.HandleAsync(new CreateProductCommand(SellerId, name, description, price, currency, stock));

    [Fact]
    public async Task Creates_the_product_for_the_seller()
    {
        var result = await Create();

        var product = Assert.Single(_repository.Products);
        Assert.Equal(product.Id, result.Value.Id);
        Assert.Equal(SellerId, result.Value.SellerId);
        Assert.Equal(199.90m, result.Value.Price);
        Assert.Equal("BRL", result.Value.Currency);
        Assert.Equal(5, result.Value.Stock);
        Assert.Equal(_clock.GetUtcNow(), result.Value.CreatedAt);
    }

    [Fact]
    public async Task Uses_the_given_currency()
    {
        var result = await Create(currency: "usd");

        Assert.Equal("USD", result.Value.Currency);
    }

    [Theory]
    [InlineData(null, "price", "price.required")]
    [InlineData(0.0, "price", "price.invalid")]
    [InlineData(10.001, "price", "price.invalid")]
    public async Task Invalid_price_is_a_validation_error(double? price, string field, string code)
    {
        var result = await Create(price: (decimal?)price);

        var error = Assert.Single(result.Errors);
        Assert.Equal((ErrorType.Validation, field, code), (error.Type, error.Field, error.Code));
        Assert.Empty(_repository.Products);
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("   ", "name")]
    public async Task Blank_name_is_a_validation_error(string name, string field)
    {
        var result = await Create(name: name);

        Assert.Equal(field, Assert.Single(result.Errors).Field);
    }

    [Fact]
    public async Task Negative_stock_is_a_validation_error()
    {
        var result = await Create(stock: -1);

        Assert.Equal("stock", Assert.Single(result.Errors).Field);
    }

    [Fact]
    public async Task Missing_stock_means_zero()
    {
        var result = await Create(stock: null);

        Assert.Equal(0, result.Value.Stock);
    }

    [Fact]
    public async Task Keeps_the_listing_details()
    {
        var result = await _handler.HandleAsync(new CreateProductCommand(SellerId, "Fone", null, 150m, null, 3)
        {
            Category = "eletronicos",
            Brand = "Sony",
            Condition = "refurbished",
            ImageUrls = ["https://cdn.example.com/fone.webp"],
            OriginalPrice = 199.90m,
            FreeShipping = true,
            WarrantyMonths = 6,
            Attributes = [new("Conexão", "Bluetooth")],
        });

        Assert.Equal(("eletronicos", "Sony", "Refurbished", 199.90m, true, 6),
            (result.Value.Category, result.Value.Brand, result.Value.Condition, result.Value.OriginalPrice, result.Value.FreeShipping, result.Value.WarrantyMonths));
        Assert.Equal("Bluetooth", Assert.Single(result.Value.Attributes).Value);
    }

    [Theory]
    [InlineData("seminovo")]
    [InlineData("1")]
    public async Task Unknown_condition_is_a_validation_error(string condition)
    {
        var result = await _handler.HandleAsync(new CreateProductCommand(SellerId, "Fone", null, 150m, null, 3) { Condition = condition });

        Assert.Equal("condition", Assert.Single(result.Errors).Field);
        Assert.Empty(_repository.Products);
    }

    [Fact]
    public async Task Invalid_detail_is_a_validation_error_on_its_field()
    {
        var result = await _handler.HandleAsync(new CreateProductCommand(SellerId, "Fone", null, 150m, null, 3) { OriginalPrice = 100m });

        var error = Assert.Single(result.Errors);
        Assert.Equal(("originalPrice", "originalPrice.invalid"), (error.Field, error.Code));
    }

    [Fact]
    public async Task Invalid_currency_is_a_validation_error_on_currency()
    {
        var result = await Create(currency: "REAL");

        Assert.Equal("currency", Assert.Single(result.Errors).Field);
    }
}
