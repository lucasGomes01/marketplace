using BuildingBlocks.Application;
using Catalog.Application.GetProduct;
using Catalog.Application.ListProducts;
using Catalog.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.UnitTests.Application;

public class QueryProductsHandlerTests
{
    private readonly InMemoryProductRepository _repository = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    private Product AddProduct(string name, decimal price = 10m, ProductDetails? details = null)
    {
        var product = Product.Create(Guid.CreateVersion7(), name, null, Money.Create(price), 1, _clock, details);
        _repository.Products.Add(product);
        _clock.Advance(TimeSpan.FromMinutes(1));
        return product;
    }

    [Fact]
    public async Task Get_returns_the_product()
    {
        var product = AddProduct("Furadeira");

        var result = await new GetProductByIdHandler(_repository).HandleAsync(new GetProductByIdQuery(product.Id));

        Assert.Equal("Furadeira", result.Value.Name);
    }

    [Fact]
    public async Task Get_unknown_product_is_not_found()
    {
        var result = await new GetProductByIdHandler(_repository).HandleAsync(new GetProductByIdQuery(Guid.CreateVersion7()));

        Assert.Equal("product.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task List_returns_newest_first_with_paging_info()
    {
        AddProduct("A");
        AddProduct("B");
        AddProduct("C");

        var result = await new ListProductsHandler(_repository).HandleAsync(new ListProductsQuery(Page: 1, PageSize: 2));

        Assert.Equal(["C", "B"], result.Value.Items.Select(p => p.Name));
        Assert.Equal((1, 2, 3), (result.Value.Page, result.Value.PageSize, result.Value.TotalCount));
    }

    [Fact]
    public async Task List_defaults_to_first_page_of_20()
    {
        var result = await new ListProductsHandler(_repository).HandleAsync(new ListProductsQuery(null, null));

        Assert.Equal((1, 20), (result.Value.Page, result.Value.PageSize));
    }

    private Task<Result<Catalog.Application.PagedResponse<Catalog.Application.ProductResponse>>> List(ListProductsQuery query) =>
        new ListProductsHandler(_repository).HandleAsync(query);

    [Fact]
    public async Task List_searches_name_and_brand_ignoring_case()
    {
        AddProduct("Furadeira de impacto");
        AddProduct("Serra circular", details: new ProductDetails { Brand = "Bosch" });
        AddProduct("Martelo");

        var byName = await List(new ListProductsQuery(1, 10) { Search = "  FURADEIRA " });
        var byBrand = await List(new ListProductsQuery(1, 10) { Search = "bosch" });

        Assert.Equal(["Furadeira de impacto"], byName.Value.Items.Select(p => p.Name));
        Assert.Equal(1, byName.Value.TotalCount);
        Assert.Equal(["Serra circular"], byBrand.Value.Items.Select(p => p.Name));
    }

    [Fact]
    public async Task List_filters_by_category_condition_price_and_free_shipping()
    {
        AddProduct("Fone", 150m, new ProductDetails { Category = "eletronicos", FreeShipping = true });
        AddProduct("TV usada", 900m, new ProductDetails { Category = "eletronicos", Condition = ProductCondition.Used });
        AddProduct("Caixa de som", 400m, new ProductDetails { Category = "eletronicos" });
        AddProduct("Furadeira", 200m, new ProductDetails { Category = "ferramentas", FreeShipping = true });

        var result = await List(new ListProductsQuery(1, 10)
        {
            Category = "ELETRONICOS", Condition = "new", MinPrice = 100m, MaxPrice = 500m, FreeShipping = true,
        });

        Assert.Equal(["Fone"], result.Value.Items.Select(p => p.Name));
    }

    [Theory]
    [InlineData("price_asc", new[] { "B", "C", "A" })]
    [InlineData("price_desc", new[] { "A", "C", "B" })]
    [InlineData("name", new[] { "A", "B", "C" })]
    [InlineData("newest", new[] { "C", "B", "A" })]
    public async Task List_sorts(string sort, string[] expected)
    {
        AddProduct("A", 30m);
        AddProduct("B", 10m);
        AddProduct("C", 20m);

        var result = await List(new ListProductsQuery(1, 10) { Sort = sort });

        Assert.Equal(expected, result.Value.Items.Select(p => p.Name));
    }

    [Theory]
    [InlineData("category", "brinquedo-raro", null, null, null, null)]
    [InlineData("condition", null, "seminovo", null, null, null)]
    [InlineData("minPrice", null, null, 50.0, 10.0, null)]
    [InlineData("minPrice", null, null, -1.0, null, null)]
    [InlineData("sort", null, null, null, null, "popular")]
    public async Task List_rejects_invalid_filters(
        string field, string? category, string? condition, double? minPrice, double? maxPrice, string? sort)
    {
        var result = await List(new ListProductsQuery(1, 10)
        {
            Category = category, Condition = condition, MinPrice = (decimal?)minPrice, MaxPrice = (decimal?)maxPrice, Sort = sort,
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal((ErrorType.Validation, field), (error.Type, error.Field));
    }

    [Fact]
    public async Task Get_returns_the_listing_details()
    {
        var product = AddProduct("Fone", 150m, new ProductDetails
        {
            Category = "eletronicos",
            Brand = "Sony",
            Condition = ProductCondition.Refurbished,
            ImageUrls = ["https://cdn.example.com/fone.webp"],
            OriginalPrice = 199.90m,
            Attributes = [new ProductAttribute("Conexão", "Bluetooth 5.3")],
        });

        var result = await new GetProductByIdHandler(_repository).HandleAsync(new GetProductByIdQuery(product.Id));

        Assert.Equal(("eletronicos", "Sony", "Refurbished", 199.90m),
            (result.Value.Category, result.Value.Brand, result.Value.Condition, result.Value.OriginalPrice));
        Assert.Equal(["https://cdn.example.com/fone.webp"], result.Value.ImageUrls);
        Assert.Equal(new Catalog.Application.ProductAttributeResponse("Conexão", "Bluetooth 5.3"), Assert.Single(result.Value.Attributes));
    }

    [Theory]
    [InlineData(0, 10, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 51, "pageSize")]
    public async Task List_rejects_invalid_paging(int page, int pageSize, string field)
    {
        var result = await new ListProductsHandler(_repository).HandleAsync(new ListProductsQuery(page, pageSize));

        var error = Assert.Single(result.Errors);
        Assert.Equal((ErrorType.Validation, field), (error.Type, error.Field));
    }
}
