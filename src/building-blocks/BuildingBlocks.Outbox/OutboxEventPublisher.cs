using System.Diagnostics;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

/// <summary>
/// <see cref="IEventPublisher"/> do outbox: <b>não fala com o Kafka</b>. Só adiciona o evento ao DbContext do serviço,
/// e ele é gravado no próximo <c>SaveChanges</c>, na mesma transação da mudança de negócio. O
/// <see cref="OutboxRelay{TContext}"/> publica depois. Por isso o handler precisa registrar o evento <b>antes</b> de
/// salvar, no mesmo escopo (mesmo DbContext) do repositório.
/// </summary>
public sealed class OutboxEventPublisher<TContext>(TContext db, TimeProvider clock, OutboxSignal signal) : IEventPublisher
    where TContext : DbContext
{
    private bool _wakesRelayOnSave;

    public async Task PublishAsync<T>(string eventType, string key, Guid eventId, T data, CancellationToken cancellationToken)
    {
        var outbox = db.Set<OutboxMessage>();
        // Mesmo evento de novo (reentrega, nova tentativa): já está registrado, publicado ou pendente.
        if (outbox.Local.Any(m => m.EventId == eventId) ||
            await outbox.AnyAsync(m => m.EventId == eventId, cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        outbox.Add(OutboxMessage.Create(eventType, key, eventId,
            KafkaMessages.Serialize(eventType, eventId, now, data), Activity.Current?.Id, now));

        if (!_wakesRelayOnSave)
        {
            // Depois do commit o relay é acordado na hora, sem esperar o próximo polling.
            db.SavedChanges += (_, _) => signal.Notify();
            _wakesRelayOnSave = true;
        }
    }
}
