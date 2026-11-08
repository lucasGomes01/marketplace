using Payments.Domain;

namespace Payments.Application;

public sealed record PayoutResponse(
    Guid Id,
    Guid OrderId,
    string Status,
    decimal Gross,
    decimal Fee,
    decimal Net,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? SentAt,
    string? ProviderReference,
    string? FailureReason)
{
    public static PayoutResponse From(Payout p) => new(
        p.Id, p.OrderId, p.Status.ToString(), p.GrossAmount, p.FeeAmount, p.NetAmount, p.Currency, p.CreatedAt,
        p.DeliveredAt, p.ScheduledFor, p.SentAt, p.ProviderReference,
        p.Status is PayoutStatus.Failed or PayoutStatus.Canceled ? p.LastError : null);
}

/// <summary>O cartão como aparece para o vendedor: nunca o número completo.</summary>
public sealed record PayoutCardResponse(string Brand, string Last4, int ExpiryMonth, int ExpiryYear, string HolderName, DateTimeOffset UpdatedAt)
{
    public const string Mastercard = "Mastercard";

    public static PayoutCardResponse From(PayoutCard c) =>
        new(Mastercard, c.Last4, c.ExpiryMonth, c.ExpiryYear, c.HolderName, c.UpdatedAt);
}
