namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Porta de consumo: reage a um evento de integração. O consumidor Kafka (BuildingBlocks.Messaging) chama isto num
/// escopo de DI novo por mensagem e só confirma o offset depois que terminar.
/// Qualquer exceção → nova tentativa; <see cref="InvalidEventException"/> → registra e pula (nunca vai dar certo).
/// Handlers precisam ser idempotentes: a entrega é at-least-once.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
{
    Task HandleAsync(Guid eventId, TEvent data, CancellationToken cancellationToken);
}

public sealed class InvalidEventException(string message) : Exception(message);
