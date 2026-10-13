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

    private Product AddProduct(string name)
    {
        var product = Product.Create(Guid.CreateVersion7(), name, null, Money.Create(10m), 1, _clock);
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
