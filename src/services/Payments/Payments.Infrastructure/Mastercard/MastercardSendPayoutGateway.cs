using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Abstractions;

namespace Payments.Infrastructure.Mastercard;

/// <summary>
/// Adaptador do <b>Mastercard Send — Disbursements</b>: paga o líquido do repasse no cartão Mastercard do vendedor.
/// <c>POST /send/v1/partners/{partnerId}/disbursements/payment</c>, assinado com OAuth 1.0a (ver <see cref="MastercardOAuthHandler"/>).
/// <list type="bullet">
/// <item>2xx → aprovado (guarda o id do disbursement) ou recusado (status DECLINED/REJECTED);</item>
/// <item>409 (referência repetida) → consulta pela referência: se já foi pago, é o mesmo pagamento;</item>
/// <item>outros 4xx → recusa definitiva com a descrição do erro;</item>
/// <item>5xx, 408, 429, timeout, sem rede → falha temporária (nova tentativa depois).</item>
/// </list>
/// Contrato conferido com a documentação pública do sandbox; confira os campos no portal ao ligar com credenciais reais.
/// </summary>
public sealed class MastercardSendPayoutGateway(
    HttpClient http, IOptions<MastercardOptions> options, ILogger<MastercardSendPayoutGateway> logger) : IPayoutGateway
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string BasePath => $"send/v1/partners/{Uri.EscapeDataString(options.Value.PartnerId)}/disbursements";

    public async Task<PayoutTransferResult> SendAsync(PayoutTransfer transfer, CancellationToken cancellationToken)
    {
        var request = new DisbursementRequest(new Disbursement(
            DisbursementReference: transfer.Reference,
            PaymentType: "BDB", // Business Disbursement: empresa pagando uma pessoa
            Amount: MinorUnits(transfer.Amount.Amount),
            Currency: transfer.Amount.Currency,
            RecipientAccountUri: $"pan:{transfer.CardNumber};exp={transfer.Expiry.Year:0000}-{transfer.Expiry.Month:00}",
            Recipient: Recipient.From(transfer.HolderName),
            PaymentOriginationCountry: options.Value.OriginationCountry));

        using var response = await CallAsync(() => http.PostAsJsonAsync($"{BasePath}/payment", request, Json, cancellationToken), cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await ResultFromAsync(response, cancellationToken);
        }
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return await FindByReferenceAsync(transfer.Reference, cancellationToken)
                ?? PayoutTransferResult.Decline(await ErrorDescriptionAsync(response, cancellationToken));
        }
        ThrowIfTransient(response);

        var reason = await ErrorDescriptionAsync(response, cancellationToken);
        logger.LogWarning("Mastercard Send declined {Reference}: {Status} {Reason}", transfer.Reference, (int)response.StatusCode, reason);
        return PayoutTransferResult.Decline(reason);
    }

    private async Task<PayoutTransferResult?> FindByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await CallAsync(
            () => http.GetAsync($"{BasePath}?ref={Uri.EscapeDataString(reference)}", cancellationToken), cancellationToken);
        ThrowIfTransient(response);
        return response.IsSuccessStatusCode ? await ResultFromAsync(response, cancellationToken) : null;
    }

    private static async Task<PayoutTransferResult> ResultFromAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<DisbursementResponse>(Json, cancellationToken);
        var disbursement = body?.Disbursement
            ?? throw new PayoutGatewayUnavailableException("Mastercard Send answered without a disbursement.");
        var status = disbursement.Status?.ToUpperInvariant();
        return status is "DECLINED" or "REJECTED" or "FAILED"
            ? PayoutTransferResult.Decline($"Mastercard Send: {status}")
            : PayoutTransferResult.Approve(disbursement.Id ?? disbursement.DisbursementReference ?? "approved");
    }

    private static async Task<HttpResponseMessage> CallAsync(Func<Task<HttpResponseMessage>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new PayoutGatewayUnavailableException("Mastercard Send is unreachable.", ex);
        }
    }

    private static void ThrowIfTransient(HttpResponseMessage response)
    {
        if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
        {
            throw new PayoutGatewayUnavailableException($"Mastercard Send answered {(int)response.StatusCode}.");
        }
    }

    /// <summary>Lê o formato de erro das APIs da Mastercard: <c>{"Errors":{"Error":[{"Description":"..."}]}}</c>.</summary>
    private static async Task<string> ErrorDescriptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var fallback = $"Mastercard Send answered {(int)response.StatusCode}.";
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (doc.RootElement.TryGetProperty("Errors", out var errors) &&
                errors.TryGetProperty("Error", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
            {
                var first = list[0];
                var description = first.TryGetProperty("Description", out var d) ? d.GetString() : null;
                var code = first.TryGetProperty("ReasonCode", out var c) ? c.GetString() : null;
                return string.Join(" — ", new[] { code, description }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } text
                    ? text
                    : fallback;
            }
        }
        catch (JsonException)
        {
        }
        return fallback;
    }

    /// <summary>Valor em centavos, como string ("80.55" → "8055").</summary>
    internal static string MinorUnits(decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    internal sealed record DisbursementRequest(Disbursement Disbursement);

    internal sealed record Disbursement(
        string DisbursementReference,
        string PaymentType,
        string Amount,
        string Currency,
        string RecipientAccountUri,
        Recipient Recipient,
        string PaymentOriginationCountry);

    internal sealed record Recipient(string FirstName, string LastName)
    {
        public static Recipient From(string holderName)
        {
            var parts = holderName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            return new Recipient(parts[0], parts.Length > 1 ? parts[1] : parts[0]);
        }
    }

    private sealed record DisbursementResponse(DisbursementResult? Disbursement);

    private sealed record DisbursementResult(string? Id, string? Status, string? DisbursementReference);
}
