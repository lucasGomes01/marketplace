namespace Catalog.Domain;

public sealed record Money
{
    public const string DefaultCurrency = "BRL";

    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0)
        {
            throw new DomainException("Amount cannot be negative.", "price");
        }
        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Amount must have at most 2 decimal places.", "price");
        }

        var normalizedCurrency = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException($"'{currency}' is not a valid ISO 4217 currency code.", "currency");
        }

        return new Money(amount, normalizedCurrency);
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
