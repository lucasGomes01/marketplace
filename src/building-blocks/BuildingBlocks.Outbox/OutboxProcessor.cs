using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Outbox;

public sealed record OutboxBatchResult(int Published, bool Failed);

/// <summary>
/// Uma passada do relay. Dispara o lote inteiro em ordem e só então espera os acks: o produtor idempotente do Kafka
/// mantém a ordem por partição com vários envios em voo, e esperar um ack por vez limitava a vazão (T-027).
/// Só conta como publicado o trecho antes do primeiro erro; o resto fica pendente e é reenviado em ordem, para que uma
/// mensagem posterior (ex.: <c>Packed</c>) nunca fique publicada sem a anterior (<c>Picked</c>).
/// </summary>
public sealed class OutboxProcessor(
    IOutboxStore store, IOutboxTransport transport, TimeProvider clock, ILogger<OutboxProcessor> logger)
{
    public async Task<OutboxBatchResult> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pending = await store.FetchPendingAsync(batchSize, cancellationToken);
        if (pending.Count == 0)
        {
            return new OutboxBatchResult(0, Failed: false);
        }

        var sends = pending.Select(message => SendAsync(message, cancellationToken)).ToList();
        try
        {
            await Task.WhenAll(sends);
        }
        catch when (sends.Any(s => s.IsFaulted))
        {
            // Cada envio é examinado abaixo.
        }

        var published = sends.TakeWhile(s => s.IsCompletedSuccessfully).Count();
        if (published > 0)
        {
            await store.MarkPublishedAsync(pending.Take(published).Select(m => m.Sequence).ToList(), clock.GetUtcNow(), cancellationToken);
        }
        if (published == pending.Count)
        {
            return new OutboxBatchResult(published, Failed: false);
        }

        var failed = pending[published];
        var error = sends[published].Exception?.GetBaseException();
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogWarning(error, "Outbox could not send {EventType} {EventId} (attempt {Attempt})",
            failed.Type, failed.EventId, failed.Attempts + 1);
        await store.MarkFailedAsync(failed.Sequence, error?.Message ?? "send failed", cancellationToken);
        return new OutboxBatchResult(published, Failed: true);
    }

    // Exceção síncrona do transporte vira Task com falha, sem impedir o envio dos outros.
    private async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        await transport.SendAsync(message, cancellationToken);
}
