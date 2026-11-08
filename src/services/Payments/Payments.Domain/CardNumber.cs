namespace Payments.Domain;

/// <summary>
/// Número de cartão <b>Mastercard</b> válido (o repasse usa o Mastercard Send, que só paga em cartões da bandeira).
/// Aceita espaços e hífens na digitação; guarda só os dígitos. Nunca aparece em <see cref="ToString"/> nem em logs.
/// </summary>
public sealed class CardNumber
{
    public const string Field = "cardNumber";

    private CardNumber(string digits) => Digits = digits;

    /// <summary>Os 16 dígitos. Só para cifrar e para o adaptador de pagamento: nunca devolver numa resposta.</summary>
    public string Digits { get; }

    public string Last4 => Digits[^4..];

    public static CardNumber Create(string? input)
    {
        var digits = new string((input ?? string.Empty).Where(c => c is not (' ' or '-')).ToArray());
        if (digits.Length == 0)
        {
            throw new DomainException("Card number is required.", Field);
        }
        if (digits.Length != 16 || !digits.All(char.IsAsciiDigit))
        {
            throw new DomainException("Card number must have 16 digits.", Field);
        }
        if (!PassesLuhn(digits))
        {
            throw new DomainException("Card number is not valid.", Field);
        }
        if (!IsMastercard(digits))
        {
            throw new DomainException("Only Mastercard cards are accepted.", Field);
        }
        return new CardNumber(digits);
    }

    /// <summary>Faixas de BIN da Mastercard: 51–55 e 2221–2720.</summary>
    public static bool IsMastercard(string digits)
    {
        var two = int.Parse(digits[..2]);
        var four = int.Parse(digits[..4]);
        return two is >= 51 and <= 55 || four is >= 2221 and <= 2720;
    }

    /// <summary>Algoritmo de Luhn (dígito verificador): dobra um dígito sim, outro não, a partir do penúltimo.</summary>
    public static bool PassesLuhn(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }

    public override string ToString() => $"•••• {Last4}";
}
