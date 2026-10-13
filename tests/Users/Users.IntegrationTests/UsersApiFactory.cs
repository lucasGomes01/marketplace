using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Users.Infrastructure.Persistence;

namespace Users.IntegrationTests;

/// <summary>
/// Sobe a API com um SQL Server real (Testcontainers). Um container para toda a coleção de testes;
/// as migrations são aplicadas no startup da API.
/// </summary>
public sealed class UsersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    Task IAsyncLifetime.InitializeAsync() => _sqlServer.StartAsync();

    /// <summary>Conexão do container, para testes que criam bancos próprios (ex.: migrations).</summary>
    public string SqlServerConnectionString => _sqlServer.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _sqlServer.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", "marketplace");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-32-chars-min");
        builder.UseSetting("Jwt:ExpirationMinutes", "60");
    }

    public const string JwtIssuer = "marketplace-users";

    /// <summary>Cadastra um usuário novo (opcionalmente já vendedor) e retorna um client autenticado.</summary>
    public async Task<(HttpClient Client, Guid UserId, string Email, string Password)> CreateAuthenticatedClient(bool seller = false)
    {
        var client = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        const string password = "Sup3r-secret";

        var registered = await client.PostAsJsonAsync("/users", new { email, name = "Test User", password });
        registered.EnsureSuccessStatusCode();
        var userId = (await registered.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (seller)
        {
            var activated = await client.PostAsync("/users/me/seller", null);
            activated.EnsureSuccessStatusCode();
            var newToken = (await activated.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        }

        return (client, userId, email, password);
    }

    private sealed record IdResponse(Guid Id);

    public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

    public async Task<T> WithDbContext<T>(Func<UsersDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<UsersDbContext>());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _sqlServer.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class UsersApiCollection : ICollectionFixture<UsersApiFactory>
{
    public const string Name = "Users API";
}
