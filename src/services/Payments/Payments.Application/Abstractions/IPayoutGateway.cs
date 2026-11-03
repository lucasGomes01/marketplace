using Payments.Domain;

namespace Payments.Application.Abstractions;

/// <summary>
/// Porta de envio do dinheiro ao cartão do vendedor. Adaptadores: Mastercard Send (sandbox) e simulado.
/// Precisa ser idempotente pela <see cref="PayoutTransfer.Reference"/>: reenviar a mesma referência não paga de novo.
/// </summary>
public interface IPayoutGateway
{
    /// <exception cref="PayoutGatewayUnavailableException">Falha temporária: tentar de novo mais tarde.</exception>
    Task<PayoutTransferResult> SendAsync(PayoutTransfer transfer, CancellationToken cancellationToken);
}

/// <summary>O que enviar. <see cref="CardNumber"/> são os dígitos decifrados: nunca registrar em log.</summary>
public sealed record PayoutTransfer(string Reference, Money Amount, string CardNumber, CardExpiry Expiry, string HolderName)
{
    public override string ToString() => $"PayoutTransfer {{ Reference = {Reference}, Amount = {Amount} }}";
}

/// <summary>Resposta definitiva: aprovado (com a referência do provedor) ou recusado (com o motivo).</summary>
public sealed record PayoutTransferResult(bool Approved, string? ProviderReference, string? DeclineReason)
{
    public static PayoutTransferResult Approve(string providerReference) => new(true, providerReference, null);

    public static PayoutTransferResult Decline(string reason) => new(false, null, reason);
}

public sealed class PayoutGatewayUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
