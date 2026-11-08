using Payments.Domain;

namespace Payments.UnitTests.Domain;

public class CardNumberTests
{
    [Theory]
    [InlineData("5555555555554444")]
    [InlineData("5105 1051 0510 5100")]
    [InlineData("2223-0000-4840-0011")]
    public void Valid_mastercard_numbers_are_accepted(string input)
    {
        var card = CardNumber.Create(input);

        Assert.Equal(16, card.Digits.Length);
        Assert.True(card.Digits.All(char.IsAsciiDigit));
    }

    [Fact]
    public void Keeps_only_the_last_four_digits_visible()
    {
        var card = CardNumber.Create("5555 5555 5555 4444");

        Assert.Equal("4444", card.Last4);
        Assert.Equal("•••• 4444", card.ToString());
        Assert.DoesNotContain("5555555555554444", card.ToString());
    }

    [Theory]
    [InlineData("4111111111111111")] // Visa (Luhn ok)
    [InlineData("378282246310005")]  // Amex (15 dígitos)
    [InlineData("6011111111111117")] // Discover
    public void Other_brands_are_rejected(string input)
    {
        var ex = Assert.Throws<DomainException>(() => CardNumber.Create(input));

        Assert.Equal(CardNumber.Field, ex.Field);
    }

    [Fact]
    public void Other_brands_with_16_digits_explain_that_only_mastercard_is_accepted()
    {
        var ex = Assert.Throws<DomainException>(() => CardNumber.Create("4111111111111111"));

        Assert.Contains("Mastercard", ex.Message);
    }

    [Theory]
    [InlineData("5555555555554445", "not valid")]
    [InlineData("5555 5555 5555", "16 digits")]
    [InlineData("5555abcd55554444", "16 digits")]
    [InlineData("", "required")]
    [InlineData(null, "required")]
    public void Invalid_numbers_are_rejected(string? input, string reason)
    {
        var ex = Assert.Throws<DomainException>(() => CardNumber.Create(input));

        Assert.Contains(reason, ex.Message);
    }

    [Theory]
    [InlineData("5100000000000000", true)]
    [InlineData("5599999999999999", true)]
    [InlineData("2221000000000000", true)]
    [InlineData("2720999999999999", true)]
    [InlineData("5000000000000000", false)]
    [InlineData("5600000000000000", false)]
    [InlineData("2220999999999999", false)]
    [InlineData("2721000000000000", false)]
    public void Mastercard_bin_ranges(string digits, bool expected)
    {
        Assert.Equal(expected, CardNumber.IsMastercard(digits));
    }
}
