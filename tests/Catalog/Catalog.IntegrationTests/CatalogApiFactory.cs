using System.Net.Http.Headers;
using BuildingBlocks.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;
using Testcontainers.MsSql;

namespace Catalog.IntegrationTests;

/// <summary>
/// API do Catalog com SQL Server real (Testcontainers). Os tokens são emitidos com a mesma lib e a mesma
/// configuração que o serviço Users usa, sem precisar subir o Users.
/// </summary>
public sealed class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    // Igual à produção (ADR-003): tópicos declarados antes, sem criação automática.
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.1.0")
        .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
        .Build();

    private static readonly string[] Topics = ["order.placed", "stock.reserved", "stock.rejected"];

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
        builder.UseSetting("Kafka:BootstrapServers", _kafka.GetBootstrapAddress());
        builder.UseSetting("Jwt:Issuer", "marketplace-users");
        builder.UseSetting("Jwt:Audience", "marketplace");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-32-chars-min");
        builder.UseSetting("Jwt:ExpirationMinutes", "60");
    }

    /// <summary>Client autenticado como um usuário novo com os papéis dados.</summary>
    public (HttpClient Client, Guid UserId) CreateClientAs(params string[] roles)
    {
        var userId = Guid.CreateVersion7();
        var token = Services.GetRequiredService<JwtTokenIssuer>().Issue(userId, $"{userId:N}@example.com", roles);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return (client, userId);
    }

    public (HttpClient Client, Guid UserId) CreateSellerClient() => CreateClientAs("Buyer", "Seller");

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _sqlServer.DisposeAsync();
        await _kafka.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogApiCollection : ICollectionFixture<CatalogApiFactory>
{
    public const string Name = "Catalog API";
}
