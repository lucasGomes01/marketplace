namespace Payments.Domain;

/// <summary>Validade do cartão (mês/ano). Vale até o último dia do mês.</summary>
public sealed record CardExpiry
{
    public const string Field = "expiry";

    public int Month { get; }
    public int Year { get; }

    private CardExpiry(int month, int year)
    {
        Month = month;
        Year = year;
    }

    /// <param name="year">Ano com 4 dígitos ou 2 (29 → 2029).</param>
    public static CardExpiry Create(int? month, int? year, TimeProvider clock)
    {
        if (month is null || year is null)
        {
            throw new DomainException("Expiry month and year are required.", Field);
        }
        if (month is < 1 or > 12)
        {
            throw new DomainException("Expiry month must be between 1 and 12.", Field);
        }
        var fullYear = year < 100 ? 2000 + year.Value : year.Value;
        var expiry = new CardExpiry(month.Value, fullYear);
        if (expiry.IsExpired(clock))
        {
            throw new DomainException("Card is expired.", Field);
        }
        if (fullYear > clock.GetUtcNow().Year + 20)
        {
            throw new DomainException("Expiry year is too far in the future.", Field);
        }
        return expiry;
    }

    /// <summary>Recria a partir do banco, sem conferir se já venceu.</summary>
    public static CardExpiry Restore(int month, int year) => new(month, year);

    public bool IsExpired(TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return Year < now.Year || (Year == now.Year && Month < now.Month);
    }

    public override string ToString() => $"{Month:00}/{Year % 100:00}";
}
