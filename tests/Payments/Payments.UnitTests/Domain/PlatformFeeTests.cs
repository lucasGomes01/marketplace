using Payments.Domain;

namespace Payments.UnitTests.Domain;

public class PlatformFeeTests
{
    [Theory]
    [InlineData(100.00, 0.10, 10.00)]
    [InlineData(89.50, 0.10, 8.95)]
    [InlineData(0.05, 0.10, 0.01)] // 0,005 → meio centavo para cima
    [InlineData(19.99, 0.125, 2.50)] // 2,49875
    [InlineData(50.00, 0, 0)]
    public void Fee_is_rounded_to_the_cent(decimal gross, decimal rate, decimal expectedFee)
    {
        var fee = PlatformFee.Of(rate).FeeOn(Money.Create(gross, "BRL"));

        Assert.Equal(expectedFee, fee.Amount);
        Assert.Equal("BRL", fee.Currency);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1)]
    [InlineData(1.5)]
    public void Rate_must_be_a_fraction(decimal rate)
    {
        Assert.Throws<DomainException>(() => PlatformFee.Of(rate));
    }
}
