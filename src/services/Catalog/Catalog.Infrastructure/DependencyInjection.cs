using BuildingBlocks.Messaging;
using Catalog.Application.Abstractions;
using Catalog.Application.ReserveStock;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CatalogDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")));

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IStockStore, StockStore>();

        // Eventos: publica stock.reserved/stock.rejected e consome order.placed (grupo do serviço).
        services.AddKafkaEventPublisher(configuration);
        services.AddKafkaEventConsumer<OrderPlaced, ReserveStockHandler>(OrderPlaced.EventType, "catalog-service");
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    public static async Task MigrateCatalogDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
    }
}
