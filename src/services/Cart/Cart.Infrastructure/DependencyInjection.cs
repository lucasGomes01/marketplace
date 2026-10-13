using Cart.Application.Abstractions;
using Cart.Infrastructure.Catalog;
using Cart.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cart.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Configuração: <c>ConnectionStrings:Default</c> e <c>Services:Catalog</c> (URL base do Catalog).</summary>
    public static IServiceCollection AddCartInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CartDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")));
        services.AddScoped<ICartRepository, CartRepository>();

        var catalogUrl = configuration["Services:Catalog"]
            ?? throw new InvalidOperationException("Missing configuration 'Services:Catalog' (Catalog base URL).");
        services.AddHttpClient<IProductCatalog, CatalogHttpClient>(http =>
        {
            http.BaseAddress = new Uri(catalogUrl.EndsWith('/') ? catalogUrl : catalogUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(5);
        });

        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    public static async Task MigrateCartDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CartDbContext>().Database.MigrateAsync();
    }
}
