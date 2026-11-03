using Microsoft.Extensions.Time.Testing;
using Payments.Application.ConvertPrice;

namespace Payments.UnitTests.Application;

public class ConvertPriceHandlerTests
{
    private readonly FakeExchangeRates _rates = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    private Task<BuildingBlocks.Application.Result<ConversionResponse>> Convert(decimal? amount, string? from, string? to) =>
        new ConvertPriceHandler(_rates, _clock).HandleAsync(new ConvertPriceQuery(amount, from, to));

    [Fact]
    public async Task Converts_with_the_provider_rate_rounded_to_the_cent()
    {
        var result = await Convert(89.50m, "brl", "usd");

        Assert.Equal(new ConversionResponse(89.50m, "BRL", "USD", 0.1834m, 16.41m, new DateOnly(2026, 11, 3), "mastercard"), result.Value);
    }

    [Fact]
    public async Task Same_currency_needs_no_rate()
    {
        var result = await Convert(10m, "BRL", "BRL");

        Assert.Equal(10m, result.Value.ConvertedAmount);
        Assert.Equal(0, _rates.Calls);
    }

    [Fact]
    public async Task Invalid_input_is_a_validation_error_per_field()
    {
        var result = await Convert(0m, "R$", null);

        Assert.Equal(["amount", "from", "to"], result.Errors.Select(e => e.Field!).ToArray());
        Assert.Equal(0, _rates.Calls);
    }

    [Fact]
    public async Task Unsupported_currency_is_a_validation_error()
    {
        var result = await Convert(10m, "BRL", "XYZ");

        var error = Assert.Single(result.Errors);
        Assert.Equal("to", error.Field);
        Assert.Equal("currency.unsupported", error.Code);
    }

    [Fact]
    public async Task Provider_down_is_unavailable()
    {
        _rates.Down = true;

        var result = await Convert(10m, "BRL", "USD");

        Assert.Equal(BuildingBlocks.Application.ErrorType.Unavailable, Assert.Single(result.Errors).Type);
    }
}
