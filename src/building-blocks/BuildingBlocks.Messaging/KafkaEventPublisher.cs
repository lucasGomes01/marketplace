using System.Diagnostics;
using BuildingBlocks.Application.Messaging;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>Ex.: <c>kafka:19092</c> (compose) ou <c>localhost:9092</c> (máquina).</summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>Quanto esperar o broker confirmar a mensagem antes de desistir.</summary>
    public int MessageTimeoutMs { get; set; } = 5000;
}

/// <summary>Adaptador Kafka da porta <see cref="IEventPublisher"/>. Um produtor por aplicação (thread-safe).</summary>
public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly TimeProvider _clock;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(IOptions<KafkaOptions> options, TimeProvider clock, ILogger<KafkaEventPublisher> logger)
    {
        _clock = clock;
        _logger = logger;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            // Confirmação de todas as réplicas e sem duplicatas por retry do próprio produtor.
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = options.Value.MessageTimeoutMs,
        }).Build();
    }

    public Task PublishAsync<T>(string eventType, string key, Guid eventId, T data, CancellationToken cancellationToken) =>
        SendAsync(eventType, key, eventId, KafkaMessages.Serialize(eventType, eventId, _clock.GetUtcNow(), data),
            parentTraceParent: null, cancellationToken);

    /// <summary>
    /// Publica um envelope já serializado (o relay do outbox usa isto). <paramref name="parentTraceParent"/> liga o
    /// span de envio ao trace de quem registrou o evento.
    /// </summary>
    public async Task SendAsync(
        string eventType, string key, Guid eventId, string payload, string? parentTraceParent, CancellationToken cancellationToken)
    {
        var message = KafkaMessages.Create(eventType, key, eventId, payload);
        using var activity = KafkaTracing.StartPublish(eventType, message.Headers, parentTraceParent);
        try
        {
            var result = await _producer.ProduceAsync(eventType, message, cancellationToken);
            _logger.LogInformation("Published {EventType} {EventId} to {TopicPartitionOffset}", eventType, eventId, result.TopicPartitionOffset);
        }
        catch (ProduceException<string, string> ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Error.Reason);
            _logger.LogError(ex, "Failed to publish {EventType} {EventId}: {Reason}", eventType, eventId, ex.Error.Reason);
            throw new EventPublishingException($"Could not publish {eventType}: {ex.Error.Reason}", ex);
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

public static class MessagingExtensions
{
    /// <summary>Registra o publicador Kafka (seção <c>Kafka</c> da configuração).</summary>
    public static IServiceCollection AddKafkaEventPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaOptions>()
            .Bind(configuration.GetSection(KafkaOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BootstrapServers), "Kafka:BootstrapServers is required.")
            .ValidateOnStart();
        services.AddSingleton<KafkaEventPublisher>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<KafkaEventPublisher>());
        return services;
    }
}
