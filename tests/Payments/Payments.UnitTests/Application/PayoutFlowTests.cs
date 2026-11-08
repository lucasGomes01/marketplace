using BuildingBlocks.Application.Messaging;
using Microsoft.Extensions.Time.Testing;
using Payments.Application;
using Payments.Application.Abstractions;
using Payments.Application.CancelSales;
using Payments.Application.DispatchPayouts;
using Payments.Application.RecordSale;
using Payments.Application.RegisterPayoutCard;
using Payments.Application.ReleasePayout;
using Payments.Domain;

namespace Payments.UnitTests.Application;

/// <summary>Do checkout ao dinheiro no cartão do vendedor, só com fakes.</summary>
public class PayoutFlowTests
{
    private const string MastercardNumber = "5555555555554444";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryPaymentsStore _store = new();
    private readonly FakeCardProtector _protector = new();
    private readonly FakePayoutGateway _gateway = new();
    private PayoutPolicy _policy = PayoutPolicy.Default;

    private readonly Guid _orderId = Guid.CreateVersion7();
    private readonly Guid _seller = Guid.CreateVersion7();
    private readonly Guid _otherSeller = Guid.CreateVersion7();

    private CartCheckedOut Checkout(params (Guid Seller, decimal Price, int Quantity)[] items) => new(
        _orderId, Guid.CreateVersion7(), Guid.CreateVersion7(),
        items.Select(i => new CheckedOutItem(Guid.CreateVersion7(), i.Seller, "Produto", i.Price, "BRL", i.Quantity)).ToList());

    private Task RecordSale(CartCheckedOut checkout) =>
        new RecordSaleHandler(_store, _policy, _clock).HandleAsync(checkout.CheckoutId, checkout, CancellationToken.None);

    private Task Deliver(Guid seller, string status = "Delivered") =>
        new ReleasePayoutHandler(_store, _store, _policy, _clock).HandleAsync(Guid.NewGuid(),
            new ShipmentStatusChanged(_orderId, Guid.CreateVersion7(), seller, status, _clock.GetUtcNow()), CancellationToken.None);

    private Task<BuildingBlocks.Application.Result<PayoutCardResponse>> RegisterCard(Guid seller, string number = MastercardNumber) =>
        new RegisterPayoutCardHandler(_store, _store, _protector, _policy, _clock)
            .HandleAsync(new RegisterPayoutCardCommand(seller, number, 12, 2029, "Maria Silva"));

    private async Task<DispatchResult> Dispatch() =>
        (await new DispatchDuePayoutsHandler(_store, _store, _protector, _gateway, _policy, _clock)
            .HandleAsync(new DispatchDuePayoutsCommand())).Value;

    private Payout PayoutOf(Guid seller) => _store.Payouts.Single(p => p.SellerId == seller);

    [Fact]
    public async Task Checkout_records_one_payout_per_seller_with_the_platform_fee()
    {
        await RecordSale(Checkout((_seller, 50m, 2), (_seller, 9.90m, 1), (_otherSeller, 89.50m, 1)));

        Assert.Equal(2, _store.Payouts.Count);
        var mine = PayoutOf(_seller);
        Assert.Equal(109.90m, mine.GrossAmount);
        Assert.Equal(10.99m, mine.FeeAmount);
        Assert.Equal(98.91m, mine.NetAmount);
        Assert.Equal(PayoutStatus.AwaitingDelivery, mine.Status);
        Assert.Equal(80.55m, PayoutOf(_otherSeller).NetAmount);
    }

    [Fact]
    public async Task Repeated_checkout_event_does_not_duplicate()
    {
        var checkout = Checkout((_seller, 50m, 1));

        await RecordSale(checkout);
        await RecordSale(checkout);

        Assert.Single(_store.Payouts);
        Assert.Equal(1, _store.Saves);
    }

    [Fact]
    public async Task Concurrent_duplicate_is_retried()
    {
        _store.FailNextSaveAsDuplicate = true;

        await Assert.ThrowsAsync<PayoutConcurrencyException>(() => RecordSale(Checkout((_seller, 50m, 1))));
    }

    [Fact]
    public async Task Checkout_without_items_is_skipped_as_invalid()
    {
        await Assert.ThrowsAsync<InvalidEventException>(() => RecordSale(Checkout()));
    }

    [Fact]
    public async Task Rejected_order_cancels_its_payouts()
    {
        await RecordSale(Checkout((_seller, 50m, 1)));

        await new CancelSalesHandler(_store, _clock).HandleAsync(Guid.NewGuid(),
            new StockRejected(_orderId, "Estoque insuficiente: Furadeira", []), CancellationToken.None);

        Assert.Equal(PayoutStatus.Canceled, PayoutOf(_seller).Status);
        Assert.Equal("Estoque insuficiente: Furadeira", PayoutOf(_seller).LastError);
    }

    [Fact]
    public async Task Steps_before_delivery_do_not_release_the_money()
    {
        await RecordSale(Checkout((_seller, 50m, 1)));
        await RegisterCard(_seller);

        await Deliver(_seller, "Shipped");

        Assert.Equal(PayoutStatus.AwaitingDelivery, PayoutOf(_seller).Status);
    }

    [Fact]
    public async Task Delivery_before_the_sale_is_known_is_retried()
    {
        await Assert.ThrowsAsync<SaleNotRecordedYetException>(() => Deliver(_seller));
    }

    [Fact]
    public async Task Delivered_order_is_paid_to_the_seller_mastercard()
    {
        await RecordSale(Checkout((_seller, 50m, 2)));
        await RegisterCard(_seller);
        await Deliver(_seller);

        var result = await Dispatch();

        Assert.Equal(1, result.Sent);
        var transfer = Assert.Single(_gateway.Transfers);
        Assert.Equal(MastercardNumber, transfer.CardNumber);
        Assert.Equal(90m, transfer.Amount.Amount);
        Assert.Equal(PayoutOf(_seller).TransferReference, transfer.Reference);
        Assert.Equal(PayoutStatus.Sent, PayoutOf(_seller).Status);
        Assert.Equal($"ref-{transfer.Reference}", PayoutOf(_seller).ProviderReference);
    }

    [Fact]
    public async Task Only_the_seller_whose_delivery_was_confirmed_is_paid()
    {
        await RecordSale(Checkout((_seller, 50m, 1), (_otherSeller, 30m, 1)));
        await RegisterCard(_seller);
        await RegisterCard(_otherSeller, "5105105105105100");

        await Deliver(_seller);
        await Dispatch();

        Assert.Equal(PayoutStatus.Sent, PayoutOf(_seller).Status);
        Assert.Equal(PayoutStatus.AwaitingDelivery, PayoutOf(_otherSeller).Status);
    }

    [Fact]
    public async Task Hold_period_delays_the_payment()
    {
        _policy = _policy with { HoldPeriod = TimeSpan.FromDays(7) };
        await RecordSale(Checkout((_seller, 50m, 1)));
        await RegisterCard(_seller);
        await Deliver(_seller);

        await Dispatch();
        Assert.Empty(_gateway.Transfers);

        _clock.Advance(TimeSpan.FromDays(7));
        await Dispatch();
        Assert.Single(_gateway.Transfers);
    }

    [Fact]
    public async Task Seller_without_card_is_paid_after_registering_one()
    {
        await RecordSale(Checkout((_seller, 50m, 1)));
        await Deliver(_seller);
        Assert.Equal(PayoutStatus.AwaitingCard, PayoutOf(_seller).Status);

        await RegisterCard(_seller);
        await Dispatch();

        Assert.Equal(PayoutStatus.Sent, PayoutOf(_seller).Status);
    }

    [Fact]
    public async Task Dispatcher_picks_up_payouts_left_waiting_while_the_card_was_being_registered()
    {
        await RecordSale(Checkout((_seller, 50m, 1)));
        await Deliver(_seller); // sem cartão → AwaitingCard
        _store.Cards.Add(PayoutCard.Register(_seller, CardNumber.Create(MastercardNumber),
            CardExpiry.Create(12, 2029, _clock), "Maria", _protector.Protect(MastercardNumber), _clock)); // gravado "ao mesmo tempo"

        var result = await Dispatch();

        Assert.Equal(1, result.Rescheduled);
        Assert.Equal(PayoutStatus.Sent, PayoutOf(_seller).Status);
    }

    [Fact]
    public async Task Declined_payout_is_retried_with_a_new_card()
    {
        _gateway.Respond = _ => PayoutTransferResult.Decline("Recipient account ineligible");
        await RecordSale(Checkout((_seller, 50m, 1)));
        await RegisterCard(_seller);
        await Deliver(_seller);

        var first = await Dispatch();
        Assert.Equal(1, first.Declined);
        Assert.Equal(PayoutStatus.Failed, PayoutOf(_seller).Status);

        _gateway.Respond = t => PayoutTransferResult.Approve("ok");
        await RegisterCard(_seller, "5105105105105100");
        await Dispatch();

        Assert.Equal(PayoutStatus.Sent, PayoutOf(_seller).Status);
        Assert.Equal("5105105105105100", _gateway.Transfers[^1].CardNumber);
    }

    [Fact]
    public async Task Gateway_outage_retries_later_with_the_same_reference()
    {
        var calls = 0;
        _gateway.Respond = t => ++calls == 1 ? throw new PayoutGatewayUnavailableException("timeout") : PayoutTransferResult.Approve("ok");
        await RecordSale(Checkout((_seller, 50m, 1)));
        await RegisterCard(_seller);
        await Deliver(_seller);

        var first = await Dispatch();
        Assert.Equal(1, first.RetryLater);
        Assert.Equal(PayoutStatus.Scheduled, PayoutOf(_seller).Status);

        await Dispatch(); // ainda não venceu a espera
        Assert.Single(_gateway.Transfers);

        _clock.Advance(_policy.RetryDelay);
        await Dispatch();
        Assert.Equal(PayoutStatus.Sent, PayoutOf(_seller).Status);
        Assert.Equal(_gateway.Transfers[0].Reference, _gateway.Transfers[1].Reference);
    }

    [Fact]
    public async Task Expired_card_is_declined_without_calling_mastercard()
    {
        await RecordSale(Checkout((_seller, 50m, 1)));
        await RegisterCard(_seller); // 12/2029
        await Deliver(_seller);
        _clock.Advance(TimeSpan.FromDays(365 * 4));

        var result = await Dispatch();

        Assert.Equal(1, result.Declined);
        Assert.Empty(_gateway.Transfers);
    }
}
