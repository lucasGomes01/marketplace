namespace BuildingBlocks.Outbox;

/// <summary>Pendentes agora e há quanto tempo o mais antigo espera (null = nada pendente).</summary>
public sealed record OutboxStats(int Pending, DateTimeOffset? OldestPendingSince);

/// <summary>Acesso do relay à tabela do outbox.</summary>
public interface IOutboxStore
{
    /// <summary>Pendentes, na ordem de gravação.</summary>
    Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int max, CancellationToken cancellationToken);

    /// <summary>Marca um lote de uma vez (um UPDATE só).</summary>
    Task MarkPublishedAsync(IReadOnlyCollection<long> sequences, DateTimeOffset publishedAt, CancellationToken cancellationToken);

    /// <summary>Soma uma tentativa e guarda o erro; a mensagem continua pendente.</summary>
    Task MarkFailedAsync(long sequence, string error, CancellationToken cancellationToken);

    Task<OutboxStats> GetStatsAsync(CancellationToken cancellationToken);

    /// <summary>Apaga o histórico já publicado antes de <paramref name="before"/>.</summary>
    Task<int> DeletePublishedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken);
}

/// <summary>
/// Envia uma mensagem do outbox ao broker; a Task termina com a confirmação. A mensagem precisa entrar na fila do
/// produtor <b>antes</b> de o método devolver a Task: o relay dispara o lote inteiro em ordem e só depois espera os acks.
/// </summary>
public interface IOutboxTransport
{
    Task SendAsync(OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>Garante um só relay ativo por serviço, mesmo com várias cópias rodando (preserva a ordem).</summary>
public interface IOutboxLock
{
    /// <returns>O lock, ou null se outra instância já o tem.</returns>
    Task<IOutboxLease?> TryAcquireAsync(CancellationToken cancellationToken);
}

public interface IOutboxLease : IAsyncDisposable
{
    /// <summary>false se o lock se perdeu (ex.: a conexão caiu); o relay para e tenta pegá-lo de novo.</summary>
    Task<bool> IsHeldAsync(CancellationToken cancellationToken);
}
