using Microsoft.Extensions.Caching.Memory;
using Payments.Application.Abstractions;

namespace Payments.Infrastructure;

/// <summary>
/// Guarda cada par de moedas por um tempo: a Mastercard publica uma taxa por dia, então a vitrine não precisa
/// chamar a API a cada visita. Moeda não suportada também fica em cache (evita repetir a chamada).
/// </summary>
public sealed class CachedExchangeRateProvider(IExchangeRateProvider inner, IMemoryCache cache, TimeSpan duration) : IExchangeRateProvider
{
    public async Task<ExchangeRate> GetRateAsync(string from, string to, CancellationToken cancellationToken)
    {
        var key = $"fx:{from}:{to}";
        if (cache.TryGetValue(key, out object? cached))
        {
            return cached switch
            {
                ExchangeRate rate => rate,
                UnsupportedCurrencyException unsupported => throw new UnsupportedCurrencyException(unsupported.Currency),
                _ => throw new InvalidOperationException("Unexpected cache entry."),
            };
        }

        try
        {
            var rate = await inner.GetRateAsync(from, to, cancellationToken);
            cache.Set(key, rate, duration);
            return rate;
        }
        catch (UnsupportedCurrencyException ex)
        {
            cache.Set(key, ex, duration);
            throw;
        }
    }
}
