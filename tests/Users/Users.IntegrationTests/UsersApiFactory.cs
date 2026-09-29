using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Users.Api.Infrastructure;

namespace Users.IntegrationTests;

/// <summary>
/// Sobe a API com um SQL Server real (Testcontainers). Um container para toda a coleção de testes;
/// as migrations são aplicadas no startup da API.
/// </summary>
public sealed class UsersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    Task IAsyncLifetime.InitializeAsync() => _sqlServer.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _sqlServer.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }

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
