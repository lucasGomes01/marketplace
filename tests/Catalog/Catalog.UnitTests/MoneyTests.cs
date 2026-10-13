using Catalog.Domain;

namespace Catalog.UnitTests;

public class MoneyTests
{
    [Fact]
    public void Create_defaults_to_brl()
    {
        var money = Money.Create(10.5m);

        Assert.Equal(10.5m, money.Amount);
        Assert.Equal("BRL", money.Currency);
    }

    [Fact]
    public void Currency_is_normalized_to_uppercase()
    {
        Assert.Equal("USD", Money.Create(1m, " usd ").Currency);
    }

    [Fact]
    public void Zero_is_a_valid_amount()
    {
        Assert.Equal(0m, Money.Create(0m).Amount);
    }

    [Fact]
    public void Negative_amount_is_invalid()
    {
        var ex = Assert.Throws<DomainException>(() => Money.Create(-0.01m));

        Assert.Equal("price", ex.Field);
    }

    [Fact]
    public void More_than_two_decimal_places_is_invalid()
    {
        Assert.Throws<DomainException>(() => Money.Create(10.001m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("BR")]
    [InlineData("REAL")]
    [InlineData("B1L")]
    public void Currency_must_have_three_letters(string currency)
    {
        Assert.Throws<DomainException>(() => Money.Create(1m, currency));
    }

    [Fact]
    public void Equal_amounts_with_different_scale_are_equal()
    {
        Assert.Equal(Money.Create(10.5m), Money.Create(10.50m));
    }

    [Fact]
    public void Different_currencies_are_not_equal()
    {
        Assert.NotEqual(Money.Create(10m, "BRL"), Money.Create(10m, "USD"));
    }
}
