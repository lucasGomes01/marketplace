using Payments.Domain;

namespace Payments.Application.Abstractions;

/// <summary>Porta de persistência do cartão de recebimento (mesma unidade de trabalho de <see cref="IPayoutRepository"/>).</summary>
public interface IPayoutCardRepository
{
    Task<PayoutCard?> FindAsync(Guid sellerId, CancellationToken cancellationToken);

    void Add(PayoutCard card);
}
