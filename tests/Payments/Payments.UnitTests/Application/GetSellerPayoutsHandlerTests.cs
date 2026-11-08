using Microsoft.Extensions.Time.Testing;
using Payments.Application;
using Payments.Application.GetPayouts;
using Payments.Domain;

namespace Payments.UnitTests.Application;

public class GetSellerPayoutsHandlerTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryPaymentsStore _store = new();
    private readonly Guid _seller = Guid.CreateVersion7();

    private Payout Add(decimal gross, Guid? seller = null)
    {
        var payout = Payout.ForSale(Guid.CreateVersion7(), seller ?? _seller, Money.Create(gross, "BRL"), PlatformFee.Of(0.1m), _clock);
        _store.Payouts.Add(payout);
        _clock.Advance(TimeSpan.FromMinutes(1));
        return payout;
    }

    [Fact]
    public async Task Lists_only_the_seller_payouts_newest_first_with_totals()
    {
        var old = Add(100m);
        old.MarkDelivered(_clock.GetUtcNow(), true, TimeSpan.Zero, _clock);
        old.MarkSent("ref", _clock);
        var canceled = Add(30m);
        canceled.Cancel("sem estoque", _clock);
        var recent = Add(50m);
        Add(70m, seller: Guid.CreateVersion7());

        var result = await new GetSellerPayoutsHandler(_store, _store).HandleAsync(new GetSellerPayoutsQuery(_seller));

        Assert.Null(result.Value.Card);
        Assert.Equal([recent.Id, canceled.Id, old.Id], result.Value.Payouts.Select(p => p.Id).ToArray());
        Assert.Equal(new PayoutTotals("BRL", Pending: 45m, Sent: 90m), Assert.Single(result.Value.Totals));
        Assert.Equal("sem estoque", result.Value.Payouts[1].FailureReason);
    }
}
