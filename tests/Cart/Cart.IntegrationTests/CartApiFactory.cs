using System.Collections.Concurrent;
using System.Net.Http.Headers;
using BuildingBlocks.Auth;
using Cart.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Cart.IntegrationTests;

/// <summary>Catalog falso: troca só o adaptador HTTP; todo o resto (EF, SQL Server, auth) é real.</summary>
public sealed class FakeCatalog : IProductCatalog
{
    private readonly ConcurrentDictionary<Guid, CatalogProduct> _products = new();

    public bool Unavailable { get; set; }

    public CatalogProduct Add(decimal price = 199.90m, int stock = 5, Guid? sellerId = null, string name = "Furadeira")
    {
        var product = new CatalogProduct(Guid.CreateVersion7(), sellerId ?? Guid.CreateVersion7(), name, price, "BRL", stock);
        _products[product.Id] = product;
        return product;
    }

    public Task<CatalogProduct?> FindAsync(Guid productId, CancellationToken cancellationToken) =>
        Unavailable
            ? throw new CatalogUnavailableException(new HttpRequestException("down"))
            : Task.FromResult(_products.GetValueOrDefault(productId));
}

public sealed class CartApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public FakeCatalog Catalog { get; } = new();

    Task IAsyncLifetime.InitializeAsync() => _sqlServer.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _sqlServer.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Services:Catalog", "http://catalog.unused.test");
        builder.UseSetting("Jwt:Issuer", "marketplace-users");
        builder.UseSetting("Jwt:Audience", "marketplace");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-32-chars-min");
        builder.UseSetting("Jwt:ExpirationMinutes", "60");
        builder.ConfigureTestServices(services => services.AddSingleton<IProductCatalog>(Catalog));
    }

    /// <summary>Client autenticado como um comprador novo (toda conta tem o papel Buyer).</summary>
    public (HttpClient Client, Guid BuyerId) CreateBuyerClient()
    {
        var buyerId = Guid.CreateVersion7();
        var token = Services.GetRequiredService<JwtTokenIssuer>().Issue(buyerId, $"{buyerId:N}@example.com", ["Buyer"]);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return (client, buyerId);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _sqlServer.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class CartApiCollection : ICollectionFixture<CartApiFactory>
{
    public const string Name = "Cart API";
}
