using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
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

        // Eventos: stock.reserved/stock.rejected vão pelo outbox (mesma transação da reserva) e o consumo de
        // order.placed usa o grupo do serviço.
        services.AddOutbox<CatalogDbContext>(configuration);
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
