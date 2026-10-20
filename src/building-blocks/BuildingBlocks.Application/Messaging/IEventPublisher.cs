namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Porta para publicar eventos de integração. A Application depende só disto; o Kafka fica na Infrastructure
/// (BuildingBlocks.Messaging). Contratos e tópicos em <c>.ai/context/events.md</c>.
/// <para>
/// Nos serviços com outbox (ADR-005) a implementação só <b>registra</b> o evento no DbContext: chame antes de salvar,
/// no mesmo escopo do repositório, para que evento e dado sejam gravados na mesma transação.
/// </para>
/// </summary>
public interface IEventPublisher
{
    /// <param name="eventType">Tipo do evento = nome do tópico (ex.: <c>cart.checked-out</c>).</param>
    /// <param name="key">Chave da mensagem: id do agregado, para manter a ordem por agregado.</param>
    /// <param name="eventId">Id do evento; consumidores usam para ignorar duplicatas.</param>
    /// <exception cref="EventPublishingException">O broker não aceitou o evento (só na publicação direta).</exception>
    Task PublishAsync<T>(string eventType, string key, Guid eventId, T data, CancellationToken cancellationToken);
}

public sealed class EventPublishingException(string message, Exception? inner = null) : Exception(message, inner);
