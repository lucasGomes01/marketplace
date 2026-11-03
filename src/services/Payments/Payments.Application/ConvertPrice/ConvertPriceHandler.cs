using BuildingBlocks.Application;
using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.ConvertPrice;

public sealed record ConvertPriceQuery(decimal? Amount, string? From, string? To);

/// <param name="Source">De onde veio a cotação: <c>mastercard</c> ou <c>simulated</c>.</param>
public sealed record ConversionResponse(
    decimal Amount, string From, string To, decimal Rate, decimal ConvertedAmount, DateOnly RateDate, string Source);

/// <summary>Converte um preço para outra moeda com a cotação da Mastercard (informativo: a compra é na moeda do produto).</summary>
public sealed class ConvertPriceHandler(IExchangeRateProvider rates, TimeProvider clock)
    : IHandler<ConvertPriceQuery, ConversionResponse>
{
    public const decimal MaxAmount = 1_000_000_000m;

    public async Task<Result<ConversionResponse>> HandleAsync(ConvertPriceQuery query, CancellationToken cancellationToken = default)
    {
        var errors = new List<Error>();
        if (query.Amount is not { } amount || amount <= 0 || amount > MaxAmount)
        {
            errors.Add(Error.Validation("amount", "amount.invalid", "Amount must be greater than zero."));
            amount = 0;
        }
        var from = Currency(query.From, "from", errors);
        var to = Currency(query.To, "to", errors);
        if (errors.Count > 0)
        {
            return Result<ConversionResponse>.Failure(errors);
        }

        if (from == to)
        {
            return new ConversionResponse(amount, from!, to!, 1m, amount, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), "identity");
        }

        ExchangeRate rate;
        try
        {
            rate = await rates.GetRateAsync(from!, to!, cancellationToken);
        }
        catch (UnsupportedCurrencyException ex)
        {
            var field = ex.Currency == from ? "from" : "to";
            return Error.Validation(field, "currency.unsupported", $"Currency '{ex.Currency}' is not supported.");
        }
        catch (ExchangeRateUnavailableException)
        {
            return Error.Unavailable("exchange_rate.unavailable", "Exchange rates are unavailable right now.");
        }

        var converted = decimal.Round(amount * rate.Rate, 2, MidpointRounding.AwayFromZero);
        return new ConversionResponse(amount, from!, to!, rate.Rate, converted, rate.Date, rate.Source);
    }

    private static string? Currency(string? value, string field, List<Error> errors)
    {
        try
        {
            return CurrencyCode.Normalize(value, field);
        }
        catch (DomainException ex)
        {
            errors.Add(Error.Validation(field, "currency.invalid", ex.Message));
            return null;
        }
    }
}
