using System.Collections.Concurrent;
using Payments.Application.Abstractions;

namespace Payments.Infrastructure.Simulated;

/// <summary>
/// Mastercard Send de mentira, para desenvolvimento, CI e E2E (sem credenciais). Aprova qualquer Mastercard, exceto
/// o cartão de teste <c>5105 1051 0510 5100</c>, que é recusado como "conta do recebedor não elegível" (igual ao
/// cenário de recusa do sandbox). Idempotente pela referência, como a API real.
/// </summary>
public sealed class SimulatedPayoutGateway : IPayoutGateway
{
    public const string DeclinedCard = "5105105105105100";

    private readonly ConcurrentDictionary<string, PayoutTransferResult> _sent = new();

    public Task<PayoutTransferResult> SendAsync(PayoutTransfer transfer, CancellationToken cancellationToken) =>
        Task.FromResult(_sent.GetOrAdd(transfer.Reference, _ => transfer.CardNumber == DeclinedCard
            ? PayoutTransferResult.Decline("Simulação: conta do recebedor não elegível (RECEIVER_INELIGIBLE).")
            : PayoutTransferResult.Approve($"sim_{transfer.Reference[3..19].ToLowerInvariant()}")));
}
