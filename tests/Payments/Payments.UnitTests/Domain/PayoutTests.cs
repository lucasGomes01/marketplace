using Microsoft.Extensions.Time.Testing;
using Payments.Domain;

namespace Payments.UnitTests.Domain;

public class PayoutTests
{
    private static readonly TimeSpan NoHold = TimeSpan.Zero;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    private Payout NewPayout(decimal gross = 100m, decimal rate = 0.10m) =>
        Payout.ForSale(Guid.CreateVersion7(), Guid.CreateVersion7(), Money.Create(gross, "BRL"), PlatformFee.Of(rate), _clock);

    private Payout ScheduledPayout()
    {
        var payout = NewPayout();
        payout.MarkDelivered(_clock.GetUtcNow(), sellerHasCard: true, NoHold, _clock);
        return payout;
    }

    [Fact]
    public void Sale_splits_gross_into_platform_fee_and_seller_net()
    {
        var payout = NewPayout(gross: 89.50m, rate: 0.10m);

        Assert.Equal(PayoutStatus.AwaitingDelivery, payout.Status);
        Assert.Equal(89.50m, payout.GrossAmount);
        Assert.Equal(8.95m, payout.FeeAmount);
        Assert.Equal(80.55m, payout.NetAmount);
        Assert.Equal(payout.GrossAmount, payout.FeeAmount + payout.NetAmount);
    }

    [Fact]
    public void Sale_must_have_a_positive_amount()
    {
        Assert.Throws<DomainException>(() => NewPayout(gross: 0m));
    }

    [Fact]
    public void Delivery_schedules_the_payout_after_the_hold_period()
    {
        var payout = NewPayout();
        var deliveredAt = _clock.GetUtcNow();

        var changed = payout.MarkDelivered(deliveredAt, sellerHasCard: true, TimeSpan.FromDays(7), _clock);

        Assert.True(changed);
        Assert.Equal(PayoutStatus.Scheduled, payout.Status);
        Assert.Equal(deliveredAt.AddDays(7), payout.ScheduledFor);
        Assert.False(payout.IsDue(_clock));
        _clock.Advance(TimeSpan.FromDays(7));
        Assert.True(payout.IsDue(_clock));
    }

    [Fact]
    public void Delivery_without_a_card_waits_for_the_card()
    {
        var payout = NewPayout();

        payout.MarkDelivered(_clock.GetUtcNow(), sellerHasCard: false, NoHold, _clock);

        Assert.Equal(PayoutStatus.AwaitingCard, payout.Status);
        Assert.Null(payout.ScheduledFor);
    }

    [Fact]
    public void Repeated_delivery_changes_nothing()
    {
        var payout = ScheduledPayout();
        var scheduledFor = payout.ScheduledFor;

        var changed = payout.MarkDelivered(_clock.GetUtcNow().AddDays(1), sellerHasCard: true, NoHold, _clock);

        Assert.False(changed);
        Assert.Equal(scheduledFor, payout.ScheduledFor);
    }

    [Fact]
    public void Registering_a_card_schedules_payouts_waiting_for_it()
    {
        var payout = NewPayout();
        payout.MarkDelivered(_clock.GetUtcNow(), sellerHasCard: false, NoHold, _clock);
        _clock.Advance(TimeSpan.FromDays(2));

        var changed = payout.CardAvailable(NoHold, _clock);

        Assert.True(changed);
        Assert.Equal(PayoutStatus.Scheduled, payout.Status);
        Assert.Equal(_clock.GetUtcNow(), payout.ScheduledFor);
    }

    [Fact]
    public void Registering_a_card_keeps_the_hold_period_counted_from_delivery()
    {
        var payout = NewPayout();
        var deliveredAt = _clock.GetUtcNow();
        payout.MarkDelivered(deliveredAt, sellerHasCard: false, TimeSpan.FromDays(7), _clock);
        _clock.Advance(TimeSpan.FromDays(2));

        payout.CardAvailable(TimeSpan.FromDays(7), _clock);

        Assert.Equal(deliveredAt.AddDays(7), payout.ScheduledFor);
    }

    [Fact]
    public void Sent_payout_keeps_the_provider_reference()
    {
        var payout = ScheduledPayout();

        payout.MarkSent("dsb_123", _clock);

        Assert.Equal(PayoutStatus.Sent, payout.Status);
        Assert.Equal("dsb_123", payout.ProviderReference);
        Assert.Equal(_clock.GetUtcNow(), payout.SentAt);
        Assert.Equal(1, payout.Attempts);
        Assert.False(payout.IsDue(_clock));
    }

    [Fact]
    public void Declined_payout_fails_until_the_seller_changes_the_card()
    {
        var payout = ScheduledPayout();

        payout.MarkDeclined("Recipient account ineligible", _clock);

        Assert.Equal(PayoutStatus.Failed, payout.Status);
        Assert.Equal("Recipient account ineligible", payout.LastError);
        Assert.True(payout.CardAvailable(NoHold, _clock));
        Assert.Equal(PayoutStatus.Scheduled, payout.Status);
        Assert.Null(payout.LastError);
    }

    [Fact]
    public void Temporary_failure_keeps_it_scheduled_for_later()
    {
        var payout = ScheduledPayout();

        payout.RetryLater("timeout", TimeSpan.FromMinutes(1), _clock);

        Assert.Equal(PayoutStatus.Scheduled, payout.Status);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(1), payout.ScheduledFor);
        Assert.Equal(1, payout.Attempts);
        Assert.False(payout.IsDue(_clock));
    }

    [Fact]
    public void Only_scheduled_payouts_can_be_sent()
    {
        var payout = NewPayout();

        Assert.Throws<InvalidOperationException>(() => payout.MarkSent("x", _clock));
    }

    [Fact]
    public void Rejected_order_cancels_the_payout_before_delivery()
    {
        var payout = NewPayout();

        Assert.True(payout.Cancel("Estoque insuficiente", _clock));

        Assert.Equal(PayoutStatus.Canceled, payout.Status);
        Assert.False(payout.MarkDelivered(_clock.GetUtcNow(), true, NoHold, _clock));
    }

    [Fact]
    public void Delivered_payout_is_not_canceled()
    {
        var payout = ScheduledPayout();

        Assert.False(payout.Cancel("late", _clock));
        Assert.Equal(PayoutStatus.Scheduled, payout.Status);
    }

    [Fact]
    public void Transfer_reference_is_stable_and_alphanumeric()
    {
        var payout = NewPayout();

        Assert.Equal(payout.TransferReference, payout.TransferReference);
        Assert.Matches("^MKT[0-9A-F]{32}$", payout.TransferReference);
    }
}
