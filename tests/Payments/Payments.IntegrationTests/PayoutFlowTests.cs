using System.Net.Http.Json;
using System.Text.Json;
using Payments.Application.CancelSales;
using Payments.Application.RecordSale;
using Payments.Application.ReleasePayout;

namespace Payments.IntegrationTests;

/// <summary>Checkout → entrega → dinheiro no cartão (simulado), pelos eventos reais do Kafka.</summary>
[Collection(PaymentsApiCollection.Name)]
public class PayoutFlowTests(PaymentsApiFactory factory)
{
    private async Task Checkout(Guid orderId, params (Guid Seller, decimal Price, int Quantity)[] items)
    {
        var checkout = new CartCheckedOut(orderId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            items.Select(i => new CheckedOutItem(Guid.CreateVersion7(), i.Seller, "Furadeira", i.Price, "BRL", i.Quantity)).ToList());
        await factory.Publisher.PublishAsync(CartCheckedOut.EventType, checkout.CartId.ToString(), orderId, checkout, CancellationToken.None);
    }

    /// <summary>
    /// Publica como o Orders (Go) publica: <c>eventId</c> em texto (<c>orderId:sellerId:status</c>), não GUID.
    /// </summary>
    private Task ShipmentStep(Guid orderId, Guid sellerId, string status) =>
        factory.PublishRawAsync(ShipmentStatusChanged.EventType, orderId.ToString(), $"{orderId}:{sellerId}:{status}",
            new ShipmentStatusChanged(orderId, Guid.CreateVersion7(), sellerId, status, DateTimeOffset.UtcNow));

    /// <summary>Espera o repasse do pedido chegar ao status (o consumo e o despachante são assíncronos).</summary>
    private static async Task<JsonElement> WaitForPayout(HttpClient seller, Guid orderId, string status, int seconds = 45)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        JsonElement? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var body = JsonDocument.Parse(await seller.GetStringAsync("/payouts")).RootElement;
            last = body.GetProperty("payouts").EnumerateArray()
                .Where(p => p.GetProperty("orderId").GetGuid() == orderId)
                .Select(p => (JsonElement?)p.Clone())
                .FirstOrDefault();
            if (last?.GetProperty("status").GetString() == status)
            {
                return last.Value;
            }
            await Task.Delay(300);
        }
        throw new Xunit.Sdk.XunitException($"Payout of {orderId} did not reach {status}; last: {last}");
    }

    [Fact]
    public async Task Delivered_order_is_paid_into_the_seller_mastercard()
    {
        var (seller, sellerId) = factory.CreateSellerClient();
        var orderId = Guid.CreateVersion7();
        (await seller.PutAsJsonAsync("/payouts/card",
            new { cardNumber = "5555555555554444", expiryMonth = 12, expiryYear = 2030, holderName = "Maria Silva" })).EnsureSuccessStatusCode();

        await Checkout(orderId, (sellerId, 50m, 2), (Guid.CreateVersion7(), 10m, 1));
        var pending = await WaitForPayout(seller, orderId, "AwaitingDelivery");
        Assert.Equal(100m, pending.GetProperty("gross").GetDecimal());
        Assert.Equal(10m, pending.GetProperty("fee").GetDecimal());
        Assert.Equal(90m, pending.GetProperty("net").GetDecimal());

        await ShipmentStep(orderId, sellerId, "Shipped");
        await ShipmentStep(orderId, sellerId, "Delivered");

        var sent = await WaitForPayout(seller, orderId, "Sent");
        Assert.StartsWith("sim_", sent.GetProperty("providerReference").GetString());
        var totals = JsonDocument.Parse(await seller.GetStringAsync("/payouts")).RootElement.GetProperty("totals")[0];
        Assert.Equal(90m, totals.GetProperty("sent").GetDecimal());
    }

    [Fact]
    public async Task Seller_without_card_is_paid_once_the_card_is_registered()
    {
        var (seller, sellerId) = factory.CreateSellerClient();
        var orderId = Guid.CreateVersion7();
        await Checkout(orderId, (sellerId, 30m, 1));
        await WaitForPayout(seller, orderId, "AwaitingDelivery");

        await ShipmentStep(orderId, sellerId, "Delivered");
        await WaitForPayout(seller, orderId, "AwaitingCard");
        (await seller.PutAsJsonAsync("/payouts/card",
            new { cardNumber = "2223000048400011", expiryMonth = 1, expiryYear = 2031, holderName = "Ana" })).EnsureSuccessStatusCode();

        await WaitForPayout(seller, orderId, "Sent");
    }

    [Fact]
    public async Task Declined_card_fails_and_shows_the_reason()
    {
        var (seller, sellerId) = factory.CreateSellerClient();
        var orderId = Guid.CreateVersion7();
        (await seller.PutAsJsonAsync("/payouts/card",
            new { cardNumber = "5105105105105100", expiryMonth = 12, expiryYear = 2030, holderName = "Maria" })).EnsureSuccessStatusCode();
        await Checkout(orderId, (sellerId, 30m, 1));
        await WaitForPayout(seller, orderId, "AwaitingDelivery");

        await ShipmentStep(orderId, sellerId, "Delivered");

        var failed = await WaitForPayout(seller, orderId, "Failed");
        Assert.Contains("RECEIVER_INELIGIBLE", failed.GetProperty("failureReason").GetString());
    }

    [Fact]
    public async Task Rejected_order_cancels_the_payout()
    {
        var (seller, sellerId) = factory.CreateSellerClient();
        var orderId = Guid.CreateVersion7();
        await Checkout(orderId, (sellerId, 30m, 1));
        await WaitForPayout(seller, orderId, "AwaitingDelivery");

        await factory.Publisher.PublishAsync(StockRejected.EventType, orderId.ToString(), orderId,
            new StockRejected(orderId, "Estoque insuficiente: Furadeira", []), CancellationToken.None);

        var canceled = await WaitForPayout(seller, orderId, "Canceled");
        Assert.Equal("Estoque insuficiente: Furadeira", canceled.GetProperty("failureReason").GetString());
    }
}
