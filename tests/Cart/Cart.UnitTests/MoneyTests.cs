using Cart.Domain;

namespace Cart.UnitTests;

public class MoneyTests
{
    [Fact]
    public void Add_sums_amounts_of_the_same_currency()
    {
        Assert.Equal(Money.Create(15.50m), Money.Create(10m).Add(Money.Create(5.50m)));
    }

    [Fact]
    public void Add_refuses_different_currencies()
    {
        var ex = Assert.Throws<DomainException>(() => Money.Create(10m, "BRL").Add(Money.Create(1m, "USD")));

        Assert.Equal("currency", ex.Field);
    }

    [Fact]
    public void Multiply_by_quantity()
    {
        Assert.Equal(Money.Create(539.70m), Money.Create(179.90m).Multiply(3));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(10.001)]
    public void Invalid_amount_is_rejected(double amount)
    {
        Assert.Throws<DomainException>(() => Money.Create((decimal)amount));
    }

    [Fact]
    public void Currency_is_normalized_and_validated()
    {
        Assert.Equal("USD", Money.Create(1m, " usd ").Currency);
        Assert.Equal("currency", Assert.Throws<DomainException>(() => Money.Create(1m, "REAL")).Field);
    }

    [Fact]
    public void Zero_in_a_currency()
    {
        Assert.Equal(Money.Create(0m, "USD"), Money.Zero("USD"));
    }
}
