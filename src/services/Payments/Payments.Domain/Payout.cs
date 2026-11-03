namespace Payments.Domain;

public enum PayoutStatus
{
    /// <summary>Venda registrada no checkout; o dinheiro só sai depois que o comprador confirma a entrega.</summary>
    AwaitingDelivery,

    /// <summary>Entregue, mas o vendedor ainda não cadastrou o cartão de recebimento.</summary>
    AwaitingCard,

    /// <summary>Pronto para enviar a partir de <see cref="Payout.ScheduledFor"/>.</summary>
    Scheduled,

    /// <summary>Pago no cartão do vendedor.</summary>
    Sent,

    /// <summary>Recusado pela Mastercard (ex.: cartão não aceita crédito). Volta a agendar quando o cartão muda.</summary>
    Failed,

    /// <summary>O pedido foi recusado (sem estoque): não há o que repassar.</summary>
    Canceled,
}

/// <summary>
/// Repasse de um vendedor por um pedido: bruto (itens dele no pedido), taxa da plataforma e líquido (o que ele recebe).
/// Um por par pedido + vendedor.
/// <code>
/// AwaitingDelivery ─entregue─▶ Scheduled ─pago─▶ Sent
///        │               └─sem cartão─▶ AwaitingCard ─cartão─▶ Scheduled
///        └─pedido recusado─▶ Canceled          Scheduled ─recusado─▶ Failed ─cartão novo─▶ Scheduled
/// </code>
/// </summary>
public sealed class Payout
{
    public const int MaxReasonLength = 500;

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid SellerId { get; private set; }
    public PayoutStatus Status { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal GrossAmount { get; private set; }
    public decimal FeeAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>A partir de quando o envio pode ser tentado (entrega + carência, ou a próxima tentativa).</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Identificador do pagamento na Mastercard (ou no simulador).</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>Motivo da recusa (<see cref="PayoutStatus.Failed"/>/<see cref="PayoutStatus.Canceled"/>) ou do último erro temporário.</summary>
    public string? LastError { get; private set; }

    public int Attempts { get; private set; }

    public Money Gross => Money.Create(GrossAmount, Currency);
    public Money Fee => Money.Create(FeeAmount, Currency);
    public Money Net => Money.Create(NetAmount, Currency);

    /// <summary>Referência única enviada à Mastercard: repetir o envio do mesmo repasse não paga duas vezes.</summary>
    public string TransferReference => $"MKT{Id:N}".ToUpperInvariant();

    private Payout()
    {
    }

    public static Payout ForSale(Guid orderId, Guid sellerId, Money gross, PlatformFee fee, TimeProvider clock)
    {
        if (gross.Amount <= 0)
        {
            throw new DomainException("Sale amount must be greater than zero.", "amount");
        }

        var feeAmount = fee.FeeOn(gross);
        var now = clock.GetUtcNow();
        return new Payout
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            SellerId = sellerId,
            Status = PayoutStatus.AwaitingDelivery,
            Currency = gross.Currency,
            GrossAmount = gross.Amount,
            FeeAmount = feeAmount.Amount,
            NetAmount = gross.Subtract(feeAmount).Amount,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// O comprador confirmou a entrega. Com cartão: agenda para <paramref name="deliveredAt"/> + <paramref name="holdPeriod"/>
    /// (carência para devolução). Sem cartão: espera o cadastro. Repetido (reentrega do evento): não faz nada.
    /// </summary>
    /// <returns>true se mudou.</returns>
    public bool MarkDelivered(DateTimeOffset deliveredAt, bool sellerHasCard, TimeSpan holdPeriod, TimeProvider clock)
    {
        if (Status != PayoutStatus.AwaitingDelivery)
        {
            return false;
        }

        DeliveredAt = deliveredAt;
        if (sellerHasCard)
        {
            Schedule(deliveredAt + holdPeriod);
        }
        else
        {
            Status = PayoutStatus.AwaitingCard;
        }
        UpdatedAt = clock.GetUtcNow();
        return true;
    }

    /// <summary>O vendedor cadastrou (ou trocou) o cartão: o que esperava cartão ou foi recusado volta a agendar.</summary>
    /// <returns>true se mudou.</returns>
    public bool CardAvailable(TimeSpan holdPeriod, TimeProvider clock)
    {
        if (Status is not (PayoutStatus.AwaitingCard or PayoutStatus.Failed))
        {
            return false;
        }

        var now = clock.GetUtcNow();
        var releaseAt = (DeliveredAt ?? now) + holdPeriod;
        Schedule(releaseAt > now ? releaseAt : now);
        UpdatedAt = now;
        return true;
    }

    public bool IsDue(TimeProvider clock) => Status == PayoutStatus.Scheduled && ScheduledFor <= clock.GetUtcNow();

    public void MarkSent(string providerReference, TimeProvider clock)
    {
        EnsureScheduled();
        Status = PayoutStatus.Sent;
        ProviderReference = providerReference;
        SentAt = clock.GetUtcNow();
        UpdatedAt = SentAt.Value;
        ScheduledFor = null;
        LastError = null;
        Attempts++;
    }

    /// <summary>Recusa definitiva da Mastercard: só tenta de novo quando o vendedor trocar o cartão.</summary>
    public void MarkDeclined(string reason, TimeProvider clock)
    {
        EnsureScheduled();
        Status = PayoutStatus.Failed;
        LastError = Truncate(reason);
        ScheduledFor = null;
        UpdatedAt = clock.GetUtcNow();
        Attempts++;
    }

    /// <summary>Falha temporária (Mastercard fora do ar, timeout): continua agendado para mais tarde.</summary>
    public void RetryLater(string error, TimeSpan wait, TimeProvider clock)
    {
        EnsureScheduled();
        var now = clock.GetUtcNow();
        Attempts++;
        LastError = Truncate(error);
        ScheduledFor = now + wait;
        UpdatedAt = now;
    }

    /// <summary>O pedido foi recusado antes da entrega. Repasse já entregue não é cancelado por aqui.</summary>
    /// <returns>true se mudou.</returns>
    public bool Cancel(string reason, TimeProvider clock)
    {
        if (Status != PayoutStatus.AwaitingDelivery)
        {
            return false;
        }

        Status = PayoutStatus.Canceled;
        LastError = Truncate(reason);
        UpdatedAt = clock.GetUtcNow();
        return true;
    }

    private void Schedule(DateTimeOffset when)
    {
        Status = PayoutStatus.Scheduled;
        ScheduledFor = when;
        LastError = null;
    }

    private void EnsureScheduled()
    {
        if (Status != PayoutStatus.Scheduled)
        {
            throw new InvalidOperationException($"Payout {Id} is {Status}, not Scheduled.");
        }
    }

    private static string Truncate(string text) =>
        text.Length <= MaxReasonLength ? text : text[..MaxReasonLength];
}
