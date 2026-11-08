using System.Net;
using System.Text.Json;

namespace Payments.IntegrationTests;

[Collection(PaymentsApiCollection.Name)]
public class ExchangeRatesApiTests(PaymentsApiFactory factory)
{
    [Fact]
    public async Task Converts_a_price_without_login()
    {
        var response = await factory.CreateClient().GetAsync("/exchange-rates/convert?amount=89.50&from=BRL&to=USD");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("USD", body.GetProperty("to").GetString());
        Assert.Equal("simulated", body.GetProperty("source").GetString());
        Assert.Equal(decimal.Round(89.50m * body.GetProperty("rate").GetDecimal(), 2, MidpointRounding.AwayFromZero),
            body.GetProperty("convertedAmount").GetDecimal());
    }

    [Theory]
    [InlineData("/exchange-rates/convert?amount=-1&from=BRL&to=USD", "amount")]
    [InlineData("/exchange-rates/convert?amount=10&from=BRL&to=XYZ", "to")]
    [InlineData("/exchange-rates/convert?amount=10&from=REAL&to=USD", "from")]
    public async Task Invalid_conversion_is_a_validation_problem(string url, string field)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), $"expected an error for {field}: {errors}");
    }
}
