using Payments.Application.Abstractions;

namespace Payments.Infrastructure.Simulated;

/// <summary>Tabela fixa de cotações (aproximadas), para desenvolvimento e testes sem credenciais da Mastercard.</summary>
public sealed class SimulatedExchangeRateProvider(TimeProvider clock) : IExchangeRateProvider
{
    /// <summary>Quantos dólares vale 1 unidade de cada moeda.</summary>
    private static readonly Dictionary<string, decimal> UsdPerUnit = new()
    {
        ["USD"] = 1m,
        ["BRL"] = 0.1834m,
        ["EUR"] = 1.0870m,
        ["GBP"] = 1.2804m,
        ["ARS"] = 0.00105m,
        ["CLP"] = 0.00106m,
        ["MXN"] = 0.0548m,
        ["JPY"] = 0.00664m,
        ["CAD"] = 0.7210m,
    };

    public static IReadOnlyCollection<string> Currencies => UsdPerUnit.Keys;

    public Task<ExchangeRate> GetRateAsync(string from, string to, CancellationToken cancellationToken)
    {
        if (!UsdPerUnit.TryGetValue(from, out var fromUsd))
        {
            throw new UnsupportedCurrencyException(from);
        }
        if (!UsdPerUnit.TryGetValue(to, out var toUsd))
        {
            throw new UnsupportedCurrencyException(to);
        }

        var rate = decimal.Round(fromUsd / toUsd, 7);
        return Task.FromResult(new ExchangeRate(from, to, rate, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), "simulated"));
    }
}
