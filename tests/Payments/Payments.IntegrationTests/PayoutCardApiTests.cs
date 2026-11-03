using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Payments.IntegrationTests;

[Collection(PaymentsApiCollection.Name)]
public class PayoutCardApiTests(PaymentsApiFactory factory)
{
    private static object Card(string number = "5555 5555 5555 4444") =>
        new { cardNumber = number, expiryMonth = 12, expiryYear = 2030, holderName = "Maria Silva" };

    [Fact]
    public async Task Health_answers()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Seller_registers_a_mastercard_and_sees_only_the_last_digits()
    {
        var (seller, sellerId) = factory.CreateSellerClient();

        var response = await seller.PutAsJsonAsync("/payouts/card", Card());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("5555555555554444", body);
        var card = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Mastercard", card.GetProperty("brand").GetString());
        Assert.Equal("4444", card.GetProperty("last4").GetString());
        Assert.Equal(12, card.GetProperty("expiryMonth").GetInt32());

        var stored = await factory.WithDbContext(db => db.PayoutCards.SingleAsync(c => c.SellerId == sellerId));
        Assert.StartsWith("v1:", stored.ProtectedNumber);
        Assert.DoesNotContain("5555555555554444", stored.ProtectedNumber);
    }

    [Fact]
    public async Task Card_of_another_brand_is_rejected_with_the_field()
    {
        var (seller, _) = factory.CreateSellerClient();

        var response = await seller.PutAsJsonAsync("/payouts/card", Card("4111 1111 1111 1111"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Contains("Mastercard", errors.GetProperty("cardNumber")[0].GetString());
    }

    [Fact]
    public async Task Only_sellers_have_payouts()
    {
        var (buyer, _) = factory.CreateClientAs("Buyer");

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/payouts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync("/payouts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PutAsJsonAsync("/payouts/card", Card())).StatusCode);
    }

    [Fact]
    public async Task New_seller_has_no_card_and_no_payouts()
    {
        var (seller, _) = factory.CreateSellerClient();

        var body = JsonDocument.Parse(await seller.GetStringAsync("/payouts")).RootElement;

        Assert.Equal(JsonValueKind.Null, body.GetProperty("card").ValueKind);
        Assert.Equal(0, body.GetProperty("payouts").GetArrayLength());
    }
}
