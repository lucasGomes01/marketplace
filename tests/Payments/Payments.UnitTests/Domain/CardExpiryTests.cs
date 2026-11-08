using Microsoft.Extensions.Time.Testing;
using Payments.Domain;

namespace Payments.UnitTests.Domain;

public class CardExpiryTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Card_is_valid_until_the_end_of_its_month()
    {
        var expiry = CardExpiry.Create(11, 2026, _clock);

        Assert.False(expiry.IsExpired(_clock));
        Assert.Equal("11/26", expiry.ToString());
    }

    [Fact]
    public void Two_digit_year_means_this_century()
    {
        var expiry = CardExpiry.Create(8, 29, _clock);

        Assert.Equal(2029, expiry.Year);
    }

    [Theory]
    [InlineData(10, 2026, "expired")]
    [InlineData(12, 2025, "expired")]
    [InlineData(0, 2027, "between 1 and 12")]
    [InlineData(13, 2027, "between 1 and 12")]
    [InlineData(1, 2060, "too far")]
    [InlineData(null, 2027, "required")]
    [InlineData(1, null, "required")]
    public void Invalid_expiry_is_rejected(int? month, int? year, string reason)
    {
        var ex = Assert.Throws<DomainException>(() => CardExpiry.Create(month, year, _clock));

        Assert.Contains(reason, ex.Message);
        Assert.Equal(CardExpiry.Field, ex.Field);
    }
}
