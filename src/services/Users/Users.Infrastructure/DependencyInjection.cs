using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Users.Application.Abstractions;
using Users.Infrastructure.Persistence;
using Users.Infrastructure.Security;

namespace Users.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Liga as portas da Application às implementações concretas.
    /// Requer <c>AddMarketplaceJwtAuth</c> (BuildingBlocks.Auth), que registra o <c>JwtTokenIssuer</c>.
    /// </summary>
    public static IServiceCollection AddUsersInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<UsersDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuerAdapter>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    public static async Task MigrateUsersDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<UsersDbContext>().Database.MigrateAsync();
    }
}
