using Microsoft.Extensions.Time.Testing;
using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Simulated;

namespace Payments.UnitTests.Infrastructure;

public class SimulatedPayoutGatewayTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    private static PayoutTransfer To(string card, string reference = "MKT0198A1B2C3D4E5F60718293A4B5C6D7E") =>
        new(reference, Money.Create(10m, "BRL"), card, CardExpiry.Create(1, 2030, Clock), "Maria");

    [Fact]
    public async Task Approves_mastercards_and_is_idempotent_by_reference()
    {
        var gateway = new SimulatedPayoutGateway();

        var first = await gateway.SendAsync(To("5555555555554444"), CancellationToken.None);
        var again = await gateway.SendAsync(To("5555555555554444"), CancellationToken.None);

        Assert.True(first.Approved);
        Assert.StartsWith("sim_", first.ProviderReference);
        Assert.Equal(first, again);
    }

    [Fact]
    public async Task Declines_the_ineligible_test_card()
    {
        var result = await new SimulatedPayoutGateway().SendAsync(To(SimulatedPayoutGateway.DeclinedCard), CancellationToken.None);

        Assert.False(result.Approved);
        Assert.Contains("RECEIVER_INELIGIBLE", result.DeclineReason);
    }
}
