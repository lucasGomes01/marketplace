using Microsoft.Extensions.Time.Testing;
using Payments.Domain;

namespace Payments.UnitTests.Domain;

public class PayoutCardTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Stores_only_last_four_digits_and_the_encrypted_number()
    {
        var card = PayoutCard.Register(Guid.CreateVersion7(), CardNumber.Create("5555555555554444"),
            CardExpiry.Create(12, 2029, _clock), " Maria Silva ", "v1:cipher", _clock);

        Assert.Equal("4444", card.Last4);
        Assert.Equal("Maria Silva", card.HolderName);
        Assert.Equal("v1:cipher", card.ProtectedNumber);
        Assert.Equal("12/29", card.Expiry.ToString());
    }

    [Fact]
    public void Refuses_a_plain_text_number()
    {
        Assert.Throws<ArgumentException>(() => PayoutCard.Register(Guid.CreateVersion7(),
            CardNumber.Create("5555555555554444"), CardExpiry.Create(12, 2029, _clock), "Maria", "5555555555554444", _clock));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Holder_name_is_required(string? name)
    {
        var ex = Assert.Throws<DomainException>(() => PayoutCard.Register(Guid.CreateVersion7(),
            CardNumber.Create("5555555555554444"), CardExpiry.Create(12, 2029, _clock), name, "v1:x", _clock));

        Assert.Equal("holderName", ex.Field);
    }

    [Fact]
    public void Replacing_the_card_updates_it()
    {
        var card = PayoutCard.Register(Guid.CreateVersion7(), CardNumber.Create("5555555555554444"),
            CardExpiry.Create(12, 2029, _clock), "Maria", "v1:a", _clock);
        _clock.Advance(TimeSpan.FromDays(1));

        card.Replace(CardNumber.Create("5105105105105100"), CardExpiry.Create(1, 2030, _clock), "Maria S", "v1:b", _clock);

        Assert.Equal("5100", card.Last4);
        Assert.Equal(_clock.GetUtcNow(), card.UpdatedAt);
        Assert.NotEqual(card.RegisteredAt, card.UpdatedAt);
    }
}
