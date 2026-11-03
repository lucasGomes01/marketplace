using Payments.Domain;

namespace Payments.Application;

/// <summary>Regras de repasse configuráveis (seção <c>Payouts</c>).</summary>
/// <param name="Fee">Comissão da plataforma sobre o bruto de cada vendedor.</param>
/// <param name="HoldPeriod">Carência entre a entrega e o pagamento (janela de devolução).</param>
/// <param name="RetryDelay">Espera depois de uma falha temporária da Mastercard.</param>
/// <param name="BatchSize">Quantos repasses o despachante tenta por rodada.</param>
public sealed record PayoutPolicy(PlatformFee Fee, TimeSpan HoldPeriod, TimeSpan RetryDelay, int BatchSize = 20)
{
    public static PayoutPolicy Default { get; } = new(PlatformFee.Of(0.10m), TimeSpan.Zero, TimeSpan.FromMinutes(1));
}
