namespace BuildingBlocks.Outbox;

/// <summary>
/// Aviso em memória "tem evento novo": acorda o relay logo depois do commit. Só vale na mesma instância; eventos
/// gravados por outra cópia do serviço são pegos pelo polling.
/// </summary>
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _pending = new(0, 1);

    public void Notify()
    {
        try
        {
            _pending.Release();
        }
        catch (SemaphoreFullException)
        {
            // Já havia um aviso pendente: o relay vai acordar de qualquer jeito.
        }
    }

    /// <returns>true se foi acordado por um aviso; false se o tempo acabou.</returns>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _pending.WaitAsync(timeout, cancellationToken);
}
