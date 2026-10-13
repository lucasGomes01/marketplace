using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.IntegrationTests;

[Collection(CatalogApiCollection.Name)]
public class ProductsApiTests(CatalogApiFactory factory)
{
    private sealed record ProductBody(
        Guid Id, Guid SellerId, string Name, string? Description, decimal Price, string Currency, int Stock, DateTimeOffset CreatedAt);

    private sealed record PageBody(List<ProductBody> Items, int Page, int PageSize, int TotalCount);

    private static object NewProduct(string name = "Furadeira de impacto", decimal? price = 199.90m, int? stock = 5) =>
        new { name, description = "700W, com maleta", price, stock };

    [Fact]
    public async Task Seller_creates_a_product_owned_by_them()
    {
        var (client, sellerId) = factory.CreateSellerClient();

        var response = await client.PostAsJsonAsync("/products", new { name = "Furadeira", price = 199.90m, stock = 5, sellerId = Guid.Empty });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductBody>();
        Assert.NotNull(product);
        Assert.Equal(sellerId, product.SellerId); // vem do token, não do corpo
        Assert.Equal((199.90m, "BRL", 5), (product.Price, product.Currency, product.Stock));
        Assert.Equal($"/products/{product.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Malformed_json_is_a_client_error_not_a_server_error()
    {
        var (client, _) = factory.CreateSellerClient();
        // "peças" com o "ç" em Latin-1 (0xE7): bytes que não são UTF-8 válido.
        var body = new ByteArrayContent([.. """{"name":"pe"""u8, 0xE7, .. """as","price":1}"""u8]);
        body.Headers.ContentType = new("application/json");

        var response = await client.PostAsync("/products", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_create_products()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/products", NewProduct());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Buyer_only_account_cannot_create_products()
    {
        var (client, _) = factory.CreateClientAs("Buyer");

        var response = await client.PostAsJsonAsync("/products", NewProduct());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("", 10, 1, "name")]
    [InlineData("Furadeira", 0, 1, "price")]
    [InlineData("Furadeira", 10.001, 1, "price")]
    [InlineData("Furadeira", 10, -1, "stock")]
    public async Task Invalid_product_returns_400_on_the_field(string name, double price, int stock, string field)
    {
        var (client, _) = factory.CreateSellerClient();

        var response = await client.PostAsJsonAsync("/products", NewProduct(name, (decimal)price, stock));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.True(problem!.Errors.ContainsKey(field), $"Esperava erro em '{field}', veio: {string.Join(", ", problem.Errors.Keys)}");
    }

    [Fact]
    public async Task Missing_price_returns_400()
    {
        var (client, _) = factory.CreateSellerClient();

        var response = await client.PostAsJsonAsync("/products", new { name = "Sem preço" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anyone_can_see_a_product()
    {
        var (seller, _) = factory.CreateSellerClient();
        var created = await (await seller.PostAsJsonAsync("/products", NewProduct("Serra tico-tico"))).Content.ReadFromJsonAsync<ProductBody>();

        var product = await factory.CreateClient().GetFromJsonAsync<ProductBody>($"/products/{created!.Id}");

        Assert.Equal(created, product);
    }

    [Fact]
    public async Task Unknown_product_returns_404_with_code()
    {
        var response = await factory.CreateClient().GetAsync($"/products/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("product.not_found", problem!.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task List_is_public_paged_and_newest_first()
    {
        var (seller, _) = factory.CreateSellerClient();
        var first = await (await seller.PostAsJsonAsync("/products", NewProduct("Primeiro"))).Content.ReadFromJsonAsync<ProductBody>();
        var second = await (await seller.PostAsJsonAsync("/products", NewProduct("Segundo"))).Content.ReadFromJsonAsync<ProductBody>();

        var page = await factory.CreateClient().GetFromJsonAsync<PageBody>("/products?page=1&pageSize=2");

        Assert.NotNull(page);
        Assert.Equal([second!.Id, first!.Id], page.Items.Select(p => p.Id));
        Assert.Equal((1, 2), (page.Page, page.PageSize));
        Assert.True(page.TotalCount >= 2);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=51")]
    public async Task Invalid_paging_returns_400(string query)
    {
        var response = await factory.CreateClient().GetAsync($"/products{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
