using System.Globalization;
using System.Net;
using System.Text.Json;
using Payments.Application.Abstractions;

namespace Payments.Infrastructure.Mastercard;

/// <summary>
/// Adaptador do <b>Mastercard Currency Conversion Calculator</b>:
/// <c>GET /settlement/currencyrate/conversion-rate?fxDate=0000-00-00&amp;transCurr=BRL&amp;crdhldBillCurr=USD&amp;bankFee=0&amp;transAmt=1</c>
/// (<c>0000-00-00</c> = taxa vigente; a Mastercard publica uma por dia). Resposta: <c>data.conversionRate</c>.
/// </summary>
public sealed class MastercardExchangeRateProvider(HttpClient http) : IExchangeRateProvider
{
    public async Task<ExchangeRate> GetRateAsync(string from, string to, CancellationToken cancellationToken)
    {
        var url = "settlement/currencyrate/conversion-rate" +
                  $"?fxDate=0000-00-00&transCurr={Uri.EscapeDataString(from)}&crdhldBillCurr={Uri.EscapeDataString(to)}&bankFee=0&transAmt=1";

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new ExchangeRateUnavailableException("Mastercard currency conversion is unreachable.", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                throw new UnsupportedCurrencyException(to);
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new ExchangeRateUnavailableException($"Mastercard currency conversion answered {(int)response.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("data", out var data))
            {
                throw new ExchangeRateUnavailableException("Mastercard currency conversion answered without data.");
            }
            if (data.TryGetProperty("errorCode", out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                // Ex.: moeda que a Mastercard não cota.
                var message = data.TryGetProperty("errorMessage", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                throw new UnsupportedCurrencyException(message.Contains(from, StringComparison.OrdinalIgnoreCase) ? from : to);
            }

            var rate = ReadDecimal(data.GetProperty("conversionRate"));
            var date = data.TryGetProperty("fxDate", out var fx) && DateOnly.TryParse(fx.GetString(), CultureInfo.InvariantCulture, out var d)
                ? d
                : DateOnly.FromDateTime(DateTime.UtcNow);
            return new ExchangeRate(from, to, rate, date, "mastercard");
        }
    }

    private static decimal ReadDecimal(JsonElement element) => element.ValueKind == JsonValueKind.String
        ? decimal.Parse(element.GetString()!, CultureInfo.InvariantCulture)
        : element.GetDecimal();
}
