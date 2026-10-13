using System.Collections.Concurrent;
using System.Net.Http.Headers;
using BuildingBlocks.Auth;
using Cart.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;
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

    // Igual à produção (ADR-003): sem criação automática; os tópicos são declarados antes de usar.
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.1.0")
        .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
        .Build();

    private static readonly string[] Topics = ["cart.checked-out"];

    public FakeCatalog Catalog { get; } = new();

    /// <summary>Endereço do Kafka do teste, para consumir o que a API publicou.</summary>
    public string KafkaBootstrapServers => _kafka.GetBootstrapAddress();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(_sqlServer.StartAsync(), _kafka.StartAsync());

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() }).Build();
        await admin.CreateTopicsAsync(Topics.Select(name => new TopicSpecification { Name = name, NumPartitions = 3, ReplicationFactor = 1 }));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _sqlServer.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Services:Catalog", "http://catalog.unused.test");
        builder.UseSetting("Kafka:BootstrapServers", _kafka.GetBootstrapAddress());
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
        await _kafka.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class CartApiCollection : ICollectionFixture<CartApiFactory>
{
    public const string Name = "Cart API";
}
