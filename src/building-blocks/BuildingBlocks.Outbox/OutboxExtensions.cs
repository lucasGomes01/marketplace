using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Outbox;

public static class OutboxExtensions
{
    /// <summary>
    /// Troca a publicação direta no Kafka pelo outbox: <see cref="IEventPublisher"/> passa a gravar o evento no
    /// <typeparamref name="TContext"/> (mesma transação do dado) e o <see cref="OutboxRelay{TContext}"/> publica.
    /// Requer o DbContext registrado, <c>modelBuilder.AddOutbox()</c> + migration, a seção <c>Kafka</c> e,
    /// opcionalmente, a seção <c>Outbox</c> (<see cref="OutboxOptions"/>).
    /// </summary>
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        services.AddKafkaEventPublisher(configuration);
        services.Replace(ServiceDescriptor.Scoped<IEventPublisher, OutboxEventPublisher<TContext>>());

        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<IOutboxTransport, KafkaOutboxTransport>();
        services.AddSingleton<IOutboxLock>(sp =>
        {
            using var scope = sp.CreateScope();
            var connectionString = scope.ServiceProvider.GetRequiredService<TContext>().Database.GetConnectionString()
                ?? throw new InvalidOperationException($"{typeof(TContext).Name} has no connection string for the outbox lock.");
            return new SqlServerOutboxLock(connectionString, $"outbox:{typeof(TContext).Name}");
        });
        services.AddHostedService<OutboxRelay<TContext>>();
        return services;
    }
}
