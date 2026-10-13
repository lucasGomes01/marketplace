using System.Net;
using System.Net.Http.Json;
using Cart.Application.Abstractions;
using Cart.Domain;
using Cart.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.IntegrationTests;

[Collection(CartApiCollection.Name)]
public class CartApiTests(CartApiFactory factory)
{
    private sealed record ItemBody(Guid ProductId, Guid SellerId, string ProductName, decimal UnitPrice, string Currency, int Quantity, decimal Subtotal);

    private sealed record CartBody(List<ItemBody> Items, int TotalQuantity, decimal Total, string Currency);

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions["code"]?.ToString();

    [Fact]
    public async Task Cart_requires_login()
    {
        var response = await factory.CreateClient().GetAsync("/cart");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_new_buyer_has_an_empty_cart()
    {
        var (client, _) = factory.CreateBuyerClient();

        var cart = await client.GetFromJsonAsync<CartBody>("/cart");

        Assert.Equal((0, 0m, "BRL"), (cart!.TotalQuantity, cart.Total, cart.Currency));
        Assert.Empty(cart.Items);
    }

    [Fact]
    public async Task Adding_items_uses_catalog_data_and_is_persisted()
    {
        var (client, _) = factory.CreateBuyerClient();
        var drill = factory.Catalog.Add(price: 199.90m, stock: 5, name: "Furadeira");
        var wrench = factory.Catalog.Add(price: 89.50m, stock: 10, name: "Chaves");

        await client.PostAsJsonAsync("/cart/items", new { productId = drill.Id, quantity = 2 });
        var response = await client.PostAsJsonAsync("/cart/items", new { productId = wrench.Id, quantity = 1, price = 0.01 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reloaded = await client.GetFromJsonAsync<CartBody>("/cart");
        Assert.Equal(["Furadeira", "Chaves"], reloaded!.Items.Select(i => i.ProductName));
        Assert.Equal(89.50m, reloaded.Items[1].UnitPrice); // o preço enviado no corpo é ignorado
        Assert.Equal((3, 489.30m), (reloaded.TotalQuantity, reloaded.Total));
    }

    [Fact]
    public async Task Each_buyer_has_their_own_cart()
    {
        var (alice, _) = factory.CreateBuyerClient();
        var (bob, _) = factory.CreateBuyerClient();
        var drill = factory.Catalog.Add();

        await alice.PostAsJsonAsync("/cart/items", new { productId = drill.Id });

        Assert.Empty((await bob.GetFromJsonAsync<CartBody>("/cart"))!.Items);
    }

    [Fact]
    public async Task Unknown_product_returns_404()
    {
        var (client, _) = factory.CreateBuyerClient();

        var response = await client.PostAsJsonAsync("/cart/items", new { productId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("product.not_found", await CodeOf(response));
    }

    [Fact]
    public async Task More_than_the_stock_returns_400_on_quantity()
    {
        var (client, _) = factory.CreateBuyerClient();
        var drill = factory.Catalog.Add(stock: 2);

        var response = await client.PostAsJsonAsync("/cart/items", new { productId = drill.Id, quantity = 3 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("quantity", problem!.Errors.Keys);
    }

    [Fact]
    public async Task A_seller_cannot_buy_their_own_product()
    {
        var (client, buyerId) = factory.CreateBuyerClient();
        var mine = factory.Catalog.Add(sellerId: buyerId);

        var response = await client.PostAsJsonAsync("/cart/items", new { productId = mine.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Change_quantity_and_remove_items()
    {
        var (client, _) = factory.CreateBuyerClient();
        var drill = factory.Catalog.Add(stock: 10);
        var wrench = factory.Catalog.Add(price: 10m);
        await client.PostAsJsonAsync("/cart/items", new { productId = drill.Id });
        await client.PostAsJsonAsync("/cart/items", new { productId = wrench.Id });

        var changed = await client.PutAsJsonAsync($"/cart/items/{drill.Id}", new { quantity = 4 });
        var removed = await client.DeleteAsync($"/cart/items/{wrench.Id}");

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var cart = await client.GetFromJsonAsync<CartBody>("/cart");
        var item = Assert.Single(cart!.Items);
        Assert.Equal((drill.Id, 4), (item.ProductId, item.Quantity));
    }

    [Fact]
    public async Task Removing_an_item_not_in_the_cart_returns_404()
    {
        var (client, _) = factory.CreateBuyerClient();

        var response = await client.DeleteAsync($"/cart/items/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("cart.item_not_found", await CodeOf(response));
    }

    [Fact]
    public async Task Catalog_down_returns_503()
    {
        var (client, _) = factory.CreateBuyerClient();
        factory.Catalog.Unavailable = true;
        try
        {
            var response = await client.PostAsJsonAsync("/cart/items", new { productId = Guid.CreateVersion7() });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("catalog.unavailable", await CodeOf(response));
        }
        finally
        {
            factory.Catalog.Unavailable = false;
        }
    }

    [Fact]
    public async Task Concurrent_changes_to_the_same_cart_do_not_overwrite_each_other()
    {
        var (client, buyerId) = factory.CreateBuyerClient();
        var drill = factory.Catalog.Add(stock: 50);
        await client.PostAsJsonAsync("/cart/items", new { productId = drill.Id });

        // Duas "requisições" leem o mesmo carrinho e alteram ao mesmo tempo.
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var repoA = first.ServiceProvider.GetRequiredService<ICartRepository>();
        var repoB = second.ServiceProvider.GetRequiredService<ICartRepository>();
        var cartA = (await repoA.FindByBuyerAsync(buyerId, CancellationToken.None))!;
        var cartB = (await repoB.FindByBuyerAsync(buyerId, CancellationToken.None))!;
        cartA.ChangeQuantity(drill.Id, 2, TimeProvider.System);
        cartB.ChangeQuantity(drill.Id, 7, TimeProvider.System);

        await repoA.SaveAsync(cartA, CancellationToken.None);

        await Assert.ThrowsAsync<CartConcurrencyException>(() => repoB.SaveAsync(cartB, CancellationToken.None));
        var cart = await client.GetFromJsonAsync<CartBody>("/cart");
        Assert.Equal(2, Assert.Single(cart!.Items).Quantity);
    }

    [Fact]
    public async Task Items_are_stored_in_their_own_table()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();

        var itemType = db.Model.FindEntityType(typeof(CartItem))!;

        Assert.Equal("CartItems", itemType.GetTableName());
        Assert.True(db.Model.FindEntityType(typeof(ShoppingCart))!.GetIndexes().Single().IsUnique);
    }
}
