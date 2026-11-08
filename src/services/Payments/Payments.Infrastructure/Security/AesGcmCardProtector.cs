using System.Security.Cryptography;
using System.Text;
using Payments.Application.Abstractions;

namespace Payments.Infrastructure.Security;

/// <summary>
/// Cifra o número do cartão com AES-256-GCM (confidencialidade + integridade). Formato gravado:
/// <c>v1:base64(nonce[12] | tag[16] | cifrado)</c>. A chave (32 bytes, base64) vem de <c>Payments:CardEncryptionKey</c>
/// e nunca vai para o banco. Trocar a chave torna os cartões gravados ilegíveis: o vendedor cadastra de novo.
/// </summary>
internal sealed class AesGcmCardProtector : ICardProtector
{
    private const string Version = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmCardProtector(string base64Key)
    {
        try
        {
            _key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Payments:CardEncryptionKey must be base64.", ex);
        }
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Payments:CardEncryptionKey must be 32 bytes (base64 of 32 random bytes).");
        }
    }

    public string Protect(string cardNumber)
    {
        var plain = Encoding.UTF8.GetBytes(cardNumber);
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return Version + Convert.ToBase64String(output);
    }

    public string Unprotect(string protectedCardNumber)
    {
        if (!protectedCardNumber.StartsWith(Version, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unknown card protection format.");
        }
        var data = Convert.FromBase64String(protectedCardNumber[Version.Length..]);
        var plain = new byte[data.Length - NonceSize - TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }
}
