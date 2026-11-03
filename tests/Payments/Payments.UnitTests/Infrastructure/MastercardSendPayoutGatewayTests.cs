using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Mastercard;

namespace Payments.UnitTests.Infrastructure;

public class MastercardSendPayoutGatewayTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    private static readonly PayoutTransfer Transfer = new(
        "MKT0198A1B2C3D4E5F60718293A4B5C6D7E", Money.Create(80.55m, "BRL"), "5555555555554444",
        CardExpiry.Create(8, 2029, Clock), "Maria da Silva");

    private static (MastercardSendPayoutGateway Gateway, StubHttpHandler Stub) Create(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        var http = new HttpClient(stub) { BaseAddress = new Uri("https://sandbox.api.mastercard.com/") };
        var options = Options.Create(new MastercardOptions { PartnerId = "ptnr_123", OriginationCountry = "BRA" });
        return (new MastercardSendPayoutGateway(http, options, NullLogger<MastercardSendPayoutGateway>.Instance), stub);
    }

    private static HttpResponseMessage Approved(string status = "APPROVED") => StubHttpHandler.Json(HttpStatusCode.Created,
        $$$"""{"disbursement":{"id":"dsb_42","disbursement_reference":"MKT...","status":"{{{status}}}"}}""");

    [Fact]
    public async Task Posts_the_disbursement_in_minor_units_to_the_partner_endpoint()
    {
        var (gateway, stub) = Create((_, _) => Approved());

        var result = await gateway.SendAsync(Transfer, CancellationToken.None);

        Assert.Equal(PayoutTransferResult.Approve("dsb_42"), result);
        var (request, body) = stub.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://sandbox.api.mastercard.com/send/v1/partners/ptnr_123/disbursements/payment", request.RequestUri!.ToString());
        var disbursement = JsonDocument.Parse(body!).RootElement.GetProperty("disbursement");
        Assert.Equal(Transfer.Reference, disbursement.GetProperty("disbursement_reference").GetString());
        Assert.Equal("BDB", disbursement.GetProperty("payment_type").GetString());
        Assert.Equal("8055", disbursement.GetProperty("amount").GetString());
        Assert.Equal("BRL", disbursement.GetProperty("currency").GetString());
        Assert.Equal("pan:5555555555554444;exp=2029-08", disbursement.GetProperty("recipient_account_uri").GetString());
        Assert.Equal("Maria", disbursement.GetProperty("recipient").GetProperty("first_name").GetString());
        Assert.Equal("da Silva", disbursement.GetProperty("recipient").GetProperty("last_name").GetString());
        Assert.Equal("BRA", disbursement.GetProperty("payment_origination_country").GetString());
    }

    [Fact]
    public async Task Declined_status_is_a_decline()
    {
        var (gateway, _) = Create((_, _) => Approved("DECLINED"));

        var result = await gateway.SendAsync(Transfer, CancellationToken.None);

        Assert.False(result.Approved);
        Assert.Contains("DECLINED", result.DeclineReason);
    }

    [Fact]
    public async Task Client_error_is_a_decline_with_the_mastercard_description()
    {
        var (gateway, _) = Create((_, _) => StubHttpHandler.Json(HttpStatusCode.BadRequest,
            """{"Errors":{"Error":[{"Source":"recipient_account_uri","ReasonCode":"RECEIVER_INELIGIBLE","Description":"Receiver not eligible","Recoverable":false}]}}"""));

        var result = await gateway.SendAsync(Transfer, CancellationToken.None);

        Assert.Equal(PayoutTransferResult.Decline("RECEIVER_INELIGIBLE — Receiver not eligible"), result);
    }

    [Fact]
    public async Task Duplicate_reference_looks_up_the_existing_disbursement()
    {
        var (gateway, stub) = Create((request, _) => request.Method == HttpMethod.Post
            ? StubHttpHandler.Json(HttpStatusCode.Conflict, """{"Errors":{"Error":[{"ReasonCode":"DUPLICATE_REQUEST"}]}}""")
            : StubHttpHandler.Json(HttpStatusCode.OK, """{"disbursement":{"id":"dsb_first","status":"APPROVED"}}"""));

        var result = await gateway.SendAsync(Transfer, CancellationToken.None);

        Assert.Equal(PayoutTransferResult.Approve("dsb_first"), result);
        Assert.Equal($"https://sandbox.api.mastercard.com/send/v1/partners/ptnr_123/disbursements?ref={Transfer.Reference}",
            stub.Requests[1].Request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Server_errors_are_temporary(HttpStatusCode status)
    {
        var (gateway, _) = Create((_, _) => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<PayoutGatewayUnavailableException>(() => gateway.SendAsync(Transfer, CancellationToken.None));
    }

    [Fact]
    public async Task Network_failure_is_temporary()
    {
        var (gateway, _) = Create((_, _) => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<PayoutGatewayUnavailableException>(() => gateway.SendAsync(Transfer, CancellationToken.None));
    }

    [Fact]
    public void Transfer_never_prints_the_card_number()
    {
        Assert.DoesNotContain("5555555555554444", Transfer.ToString());
    }

    [Theory]
    [InlineData(80.55, "8055")]
    [InlineData(0.01, "1")]
    [InlineData(1234.50, "123450")]
    public void Amounts_go_in_cents(decimal amount, string expected)
    {
        Assert.Equal(expected, MastercardSendPayoutGateway.MinorUnits(amount));
    }
}
