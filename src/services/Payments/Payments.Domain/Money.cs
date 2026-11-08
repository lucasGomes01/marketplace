namespace Payments.Domain;

/// <summary>Valor em uma moeda ISO 4217, com no máximo 2 casas decimais.</summary>
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency, string field = "amount")
    {
        if (amount < 0)
        {
            throw new DomainException("Amount cannot be negative.", field);
        }
        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Amount must have at most 2 decimal places.", field);
        }

        return new Money(amount, CurrencyCode.Normalize(currency));
    }

    public Money Subtract(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new DomainException($"Cannot subtract {other.Currency} from {Currency}.", "currency");
        }
        return Create(Amount - other.Amount, Currency);
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}

public static class CurrencyCode
{
    /// <summary>"brl " → "BRL". Três letras ASCII (ISO 4217); qualquer outra coisa é inválida.</summary>
    public static string Normalize(string? currency, string field = "currency")
    {
        var normalized = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException($"'{currency}' is not a valid ISO 4217 currency code.", field);
        }
        return normalized;
    }
}
