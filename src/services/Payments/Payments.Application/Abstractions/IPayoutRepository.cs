using Payments.Domain;

namespace Payments.Application.Abstractions;

/// <summary>
/// Porta de persistência dos repasses. Compartilha a unidade de trabalho com <see cref="IPayoutCardRepository"/>:
/// um <see cref="SaveChangesAsync"/> grava tudo o que foi alterado, numa transação.
/// </summary>
public interface IPayoutRepository
{
    Task<Payout?> FindAsync(Guid orderId, Guid sellerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Payout>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Repasses do vendedor, mais recentes primeiro.</summary>
    Task<IReadOnlyList<Payout>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken);

    /// <summary>Do vendedor, esperando cartão (<c>AwaitingCard</c>) ou recusados (<c>Failed</c>).</summary>
    Task<IReadOnlyList<Payout>> ListWaitingForCardAsync(Guid sellerId, CancellationToken cancellationToken);

    /// <summary>
    /// <c>AwaitingCard</c> de vendedores que já têm cartão (o cadastro e a entrega podem ter acontecido ao mesmo tempo).
    /// </summary>
    Task<IReadOnlyList<Payout>> ListAwaitingCardWithCardAsync(int limit, CancellationToken cancellationToken);

    /// <summary><c>Scheduled</c> com <c>ScheduledFor</c> já alcançado, os mais antigos primeiro.</summary>
    Task<IReadOnlyList<Payout>> ListDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    void Add(Payout payout);

    /// <exception cref="DuplicatePayoutException">Já existe repasse para o mesmo pedido e vendedor.</exception>
    /// <exception cref="PayoutConcurrencyException">Outra operação alterou o mesmo repasse.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class DuplicatePayoutException() : Exception("A payout for this order and seller already exists.");

public sealed class PayoutConcurrencyException() : Exception("The payout was changed concurrently.");
