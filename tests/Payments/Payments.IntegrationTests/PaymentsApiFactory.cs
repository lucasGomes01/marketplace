using System.Net.Http.Headers;
using BuildingBlocks.Auth;
using BuildingBlocks.Messaging;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Payments.Infrastructure.Persistence;
using Testcontainers.Kafka;
using Testcontainers.MsSql;

namespace Payments.IntegrationTests;

/// <summary>
/// API do Payments com SQL Server e Kafka reais (Testcontainers), provedores simulados e despachante rodando a cada
/// segundo. Os tokens são emitidos com a mesma lib e configuração do serviço Users.
/// </summary>
public sealed class PaymentsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    // Igual à produção (ADR-003): tópicos declarados antes, sem criação automática.
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.1.0")
        .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
        .Build();

    private static readonly string[] Topics = ["cart.checked-out", "stock.rejected", "shipment.status-changed"];

    private KafkaEventPublisher? _publisher;

    /// <summary>Faz o papel do Cart, do Catalog e do Orders: publica direto no Kafka.</summary>
    public KafkaEventPublisher Publisher => _publisher ??= new KafkaEventPublisher(
        Options.Create(new KafkaOptions { BootstrapServers = _kafka.GetBootstrapAddress() }),
        TimeProvider.System, NullLogger<KafkaEventPublisher>.Instance);

    /// <summary>Envelope montado à mão, com o <c>eventId</c> que o produtor quiser (o Orders em Go usa texto).</summary>
    public async Task PublishRawAsync<T>(string topic, string key, string eventId, T data)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(
            new { eventId, type = topic, occurredAt = DateTimeOffset.UtcNow, data }, KafkaMessages.Json);
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress() }).Build();
        await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = key,
            Value = payload,
            Headers =
            [
                new Header(KafkaMessages.EventTypeHeader, System.Text.Encoding.UTF8.GetBytes(topic)),
                new Header(KafkaMessages.EventIdHeader, System.Text.Encoding.UTF8.GetBytes(eventId)),
            ],
        });
    }

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
        builder.UseSetting("Payments:Provider", "Simulated");
        builder.UseSetting("Payments:CardEncryptionKey", "aW50ZWdyYXRpb24tdGVzdHMtY2FyZC1rZXktMzJieXQ=");
        builder.UseSetting("Payouts:FeeRate", "0.10");
        builder.UseSetting("Payouts:HoldDays", "0");
        builder.UseSetting("Payouts:DispatchIntervalSeconds", "1");
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

    public async Task<T> WithDbContext<T>(Func<PaymentsDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<PaymentsDbContext>());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        _publisher?.Dispose();
        await DisposeAsync();
        await _sqlServer.DisposeAsync();
        await _kafka.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PaymentsApiCollection : ICollectionFixture<PaymentsApiFactory>
{
    public const string Name = "Payments API";
}
