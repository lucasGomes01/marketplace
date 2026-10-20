using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Auth;
using BuildingBlocks.Outbox;
using Cart.Application.Abstractions;
using Cart.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Cart.IntegrationTests;

/// <summary>Cart com SQL Server real e um Kafka que não existe (endereço sem ninguém escutando).</summary>
public sealed class CartWithoutKafkaFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public FakeCatalog Catalog { get; } = new();

    Task IAsyncLifetime.InitializeAsync() => _sqlServer.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _sqlServer.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Services:Catalog", "http://catalog.unused.test");
        builder.UseSetting("Kafka:BootstrapServers", "localhost:1");
        builder.UseSetting("Kafka:MessageTimeoutMs", "1000");
        builder.UseSetting("Jwt:Issuer", "marketplace-users");
        builder.UseSetting("Jwt:Audience", "marketplace");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-32-chars-min");
        builder.UseSetting("Jwt:ExpirationMinutes", "60");
        builder.ConfigureTestServices(services => services.AddSingleton<IProductCatalog>(Catalog));
    }

    public HttpClient CreateBuyerClient()
    {
        var buyerId = Guid.CreateVersion7();
        var token = Services.GetRequiredService<JwtTokenIssuer>().Issue(buyerId, $"{buyerId:N}@example.com", ["Buyer"]);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _sqlServer.DisposeAsync();
    }
}

public class CheckoutWithoutKafkaTests(CartWithoutKafkaFactory factory) : IClassFixture<CartWithoutKafkaFactory>
{
    private sealed record CheckoutBody(Guid CheckoutId, Guid OrderId);

    private sealed record CartBody(List<JsonElement> Items);

    [Fact]
    public async Task Checkout_succeeds_with_kafka_down_and_the_event_waits_in_the_outbox()
    {
        var client = factory.CreateBuyerClient();
        var drill = factory.Catalog.Add(price: 199.90m, stock: 5);
        await client.PostAsJsonAsync("/cart/items", new { productId = drill.Id, quantity = 1 });

        var response = await client.PostAsync("/cart/checkout", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var checkout = await response.Content.ReadFromJsonAsync<CheckoutBody>();
        var cart = await client.GetFromJsonAsync<CartBody>("/cart");
        Assert.Empty(cart!.Items);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
        var message = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.EventId == checkout!.CheckoutId);
        Assert.Equal("cart.checked-out", message.Type);
        Assert.Null(message.PublishedAt);
    }
}
