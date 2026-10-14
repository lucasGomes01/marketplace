using System.Text.Json;
using BuildingBlocks.Application.Messaging;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Consumidor Kafka de um tópico, como serviço de background. Entrega at-least-once:
/// <list type="bullet">
/// <item>cada mensagem roda num escopo de DI novo e chama <see cref="IIntegrationEventHandler{TEvent}"/>;</item>
/// <item>o offset só é confirmado depois que o handler terminou (com sucesso ou mensagem inválida);</item>
/// <item>exceção → nova tentativa com backoff (0,5 s → 10 s), na mesma mensagem, sem pular a fila;</item>
/// <item>JSON quebrado ou <see cref="InvalidEventException"/> → registra e pula.</item>
/// </list>
/// </summary>
public sealed class KafkaEventConsumer<TEvent>(
    IServiceScopeFactory scopes,
    IOptions<KafkaOptions> options,
    string topic,
    string groupId,
    ILogger<KafkaEventConsumer<TEvent>> logger) : BackgroundService
    where TEvent : class
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() bloqueia a thread: roda fora do startup do host.
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(topic);
        logger.LogInformation("Consuming {Topic} as {GroupId}", topic, groupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                if (await ProcessAsync(result, stoppingToken))
                {
                    // Commit síncrono e sem o token: uma mensagem já processada é confirmada mesmo no desligamento.
                    consumer.Commit(result);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            consumer.Close();
        }
    }

    /// <returns>true se a mensagem terminou (processada ou pulada); false se o serviço está parando.</returns>
    private async Task<bool> ProcessAsync(ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        EventEnvelope<TEvent>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<TEvent>>(result.Message.Value, KafkaMessages.Json);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Skipping malformed message at {TopicPartitionOffset}", result.TopicPartitionOffset);
            return true;
        }
        if (envelope?.Data is null)
        {
            logger.LogError("Skipping message without data at {TopicPartitionOffset}", result.TopicPartitionOffset);
            return true;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();
                await handler.HandleAsync(envelope.EventId, envelope.Data, stoppingToken);
                return true;
            }
            catch (InvalidEventException ex)
            {
                logger.LogError(ex, "Skipping invalid {EventType} {EventId}", envelope.Type, envelope.EventId);
                return true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                var wait = Backoff(attempt);
                logger.LogWarning(ex, "Failed {EventType} {EventId} (attempt {Attempt}); retrying in {Wait}",
                    envelope.Type, envelope.EventId, attempt + 1, wait);
                try
                {
                    await Task.Delay(wait, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, Math.Min(attempt, 5)), 10_000));
}

public static class ConsumerExtensions
{
    /// <summary>
    /// Registra o handler (scoped) e um consumidor de <paramref name="topic"/> no grupo <paramref name="groupId"/>
    /// (convenção: nome do serviço, ex. <c>catalog-service</c>). Requer a seção <c>Kafka</c> configurada.
    /// </summary>
    public static IServiceCollection AddKafkaEventConsumer<TEvent, THandler>(
        this IServiceCollection services, string topic, string groupId)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        services.AddHostedService(sp => ActivatorUtilities.CreateInstance<KafkaEventConsumer<TEvent>>(sp, topic, groupId));
        return services;
    }
}
