using System.Net.Http.Headers;
using BuildingBlocks.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Catalog.IntegrationTests;

/// <summary>
/// API do Catalog com SQL Server real (Testcontainers). Os tokens são emitidos com a mesma lib e a mesma
/// configuração que o serviço Users usa, sem precisar subir o Users.
/// </summary>
public sealed class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    Task IAsyncLifetime.InitializeAsync() => _sqlServer.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _sqlServer.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
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
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogApiCollection : ICollectionFixture<CatalogApiFactory>
{
    public const string Name = "Catalog API";
}
