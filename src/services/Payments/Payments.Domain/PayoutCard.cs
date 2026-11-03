namespace Payments.Domain;

/// <summary>
/// Cartão Mastercard em que o vendedor recebe os repasses. Um por vendedor (cadastrar de novo substitui).
/// O número completo só existe <b>cifrado</b> (<see cref="ProtectedNumber"/>); fora daqui circulam os 4 últimos dígitos.
/// </summary>
public sealed class PayoutCard
{
    public const int HolderNameMaxLength = 100;

    public Guid SellerId { get; private set; }
    public string HolderName { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public int ExpiryMonth { get; private set; }
    public int ExpiryYear { get; private set; }

    /// <summary>Número cifrado pela Infrastructure (a chave nunca vai para o banco).</summary>
    public string ProtectedNumber { get; private set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public CardExpiry Expiry => CardExpiry.Restore(ExpiryMonth, ExpiryYear);

    private PayoutCard()
    {
    }

    public static PayoutCard Register(
        Guid sellerId, CardNumber number, CardExpiry expiry, string? holderName, string protectedNumber, TimeProvider clock)
    {
        var card = new PayoutCard { SellerId = sellerId, RegisteredAt = clock.GetUtcNow() };
        card.Replace(number, expiry, holderName, protectedNumber, clock);
        return card;
    }

    /// <summary>O vendedor trocou o cartão: os próximos repasses vão para o novo.</summary>
    public void Replace(CardNumber number, CardExpiry expiry, string? holderName, string protectedNumber, TimeProvider clock)
    {
        var name = (holderName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new DomainException("Card holder name is required.", "holderName");
        }
        if (name.Length > HolderNameMaxLength)
        {
            throw new DomainException($"Card holder name must have at most {HolderNameMaxLength} characters.", "holderName");
        }
        if (string.IsNullOrWhiteSpace(protectedNumber) || protectedNumber.Contains(number.Digits, StringComparison.Ordinal))
        {
            throw new ArgumentException("The card number must be stored encrypted.", nameof(protectedNumber));
        }

        HolderName = name;
        Last4 = number.Last4;
        ExpiryMonth = expiry.Month;
        ExpiryYear = expiry.Year;
        ProtectedNumber = protectedNumber;
        UpdatedAt = clock.GetUtcNow();
    }
}
