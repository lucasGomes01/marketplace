namespace Payments.Domain;

/// <summary>
/// Comissão da plataforma sobre a venda de um vendedor. A taxa é arredondada para o centavo (meio centavo para
/// cima) e o vendedor recebe o resto: taxa + líquido = bruto, sempre.
/// </summary>
public sealed record PlatformFee
{
    public decimal Rate { get; }

    private PlatformFee(decimal rate) => Rate = rate;

    /// <param name="rate">Fração do bruto, de 0 (sem taxa) a 1 exclusive (ex.: 0.10 = 10%).</param>
    public static PlatformFee Of(decimal rate) =>
        rate is >= 0 and < 1
            ? new PlatformFee(rate)
            : throw new DomainException("Platform fee rate must be between 0 and 1.", "feeRate");

    public Money FeeOn(Money gross) =>
        Money.Create(decimal.Round(gross.Amount * Rate, 2, MidpointRounding.AwayFromZero), gross.Currency);
}
