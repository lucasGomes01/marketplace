using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using Payments.Application.Abstractions;
using Payments.Infrastructure;
using Payments.Infrastructure.Mastercard;
using Payments.Infrastructure.Simulated;

namespace Payments.UnitTests.Infrastructure;

public class ExchangeRateProvidersTests
{
    private static MastercardExchangeRateProvider Mastercard(StubHttpHandler stub) =>
        new(new HttpClient(stub) { BaseAddress = new Uri("https://sandbox.api.mastercard.com/") });

    [Fact]
    public async Task Mastercard_rate_comes_from_the_conversion_rate_endpoint()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK,
            """{"name":"settlement-conversion-rate","date":"2026-11-03 10:00:00","data":{"conversionRate":0.1836213,"crdhldBillAmt":0.18,"fxDate":"2026-11-03","transCurr":"BRL","crdhldBillCurr":"USD","transAmt":1,"bankFee":0}}"""));

        var rate = await Mastercard(stub).GetRateAsync("BRL", "USD", CancellationToken.None);

        Assert.Equal(new ExchangeRate("BRL", "USD", 0.1836213m, new DateOnly(2026, 11, 3), "mastercard"), rate);
        Assert.Equal(
            "https://sandbox.api.mastercard.com/settlement/currencyrate/conversion-rate?fxDate=0000-00-00&transCurr=BRL&crdhldBillCurr=USD&bankFee=0&transAmt=1",
            stub.Requests.Single().Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Mastercard_error_code_means_unsupported_currency()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK,
            """{"data":{"errorCode":"104","errorMessage":"Not Found, The requested resource could not be found."}}"""));

        var ex = await Assert.ThrowsAsync<UnsupportedCurrencyException>(() => Mastercard(stub).GetRateAsync("BRL", "XYZ", CancellationToken.None));

        Assert.Equal("XYZ", ex.Currency);
    }

    [Fact]
    public async Task Mastercard_outage_is_unavailable()
    {
        var stub = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<ExchangeRateUnavailableException>(() => Mastercard(stub).GetRateAsync("BRL", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task Simulated_rates_are_consistent_both_ways()
    {
        var provider = new SimulatedExchangeRateProvider(new FakeTimeProvider());

        var brlUsd = await provider.GetRateAsync("BRL", "USD", CancellationToken.None);
        var usdBrl = await provider.GetRateAsync("USD", "BRL", CancellationToken.None);

        Assert.Equal("simulated", brlUsd.Source);
        Assert.InRange(brlUsd.Rate * usdBrl.Rate, 0.9999m, 1.0001m);
        await Assert.ThrowsAsync<UnsupportedCurrencyException>(() => provider.GetRateAsync("BRL", "XYZ", CancellationToken.None));
    }

    [Fact]
    public async Task Cache_calls_the_provider_once_per_pair()
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, """{"data":{"conversionRate":0.18,"fxDate":"2026-11-03"}}"""));
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cached = new CachedExchangeRateProvider(Mastercard(stub), memory, TimeSpan.FromHours(1));

        await cached.GetRateAsync("BRL", "USD", CancellationToken.None);
        await cached.GetRateAsync("BRL", "USD", CancellationToken.None);
        await cached.GetRateAsync("BRL", "EUR", CancellationToken.None);

        Assert.Equal(2, stub.Requests.Count);
    }
}
