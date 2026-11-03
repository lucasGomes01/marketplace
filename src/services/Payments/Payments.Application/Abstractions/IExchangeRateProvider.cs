namespace Payments.Application.Abstractions;

/// <summary>Porta de cotação de moedas. Adaptadores: Mastercard Currency Conversion (sandbox) e tabela simulada.</summary>
public interface IExchangeRateProvider
{
    /// <summary>Quanto vale 1 <paramref name="from"/> em <paramref name="to"/>.</summary>
    /// <exception cref="UnsupportedCurrencyException">Moeda que o provedor não cota.</exception>
    /// <exception cref="ExchangeRateUnavailableException">Provedor fora do ar.</exception>
    Task<ExchangeRate> GetRateAsync(string from, string to, CancellationToken cancellationToken);
}

/// <param name="Source">De onde veio a cotação: <c>mastercard</c> ou <c>simulated</c>.</param>
public sealed record ExchangeRate(string From, string To, decimal Rate, DateOnly Date, string Source);

public sealed class UnsupportedCurrencyException(string currency) : Exception($"Currency '{currency}' is not supported.")
{
    public string Currency { get; } = currency;
}

public sealed class ExchangeRateUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
