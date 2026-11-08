namespace Payments.Application.Abstractions;

/// <summary>Cifra o número do cartão antes de gravar e decifra só na hora de pagar. A chave fica fora do banco.</summary>
public interface ICardProtector
{
    string Protect(string cardNumber);

    string Unprotect(string protectedCardNumber);
}
