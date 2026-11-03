using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Payments.Infrastructure.Mastercard;

/// <summary>
/// Assinatura OAuth 1.0a usada por todas as APIs da Mastercard (RSA-SHA256 + <c>oauth_body_hash</c>).
/// <list type="number">
/// <item><c>oauth_body_hash</c> = base64(SHA-256(corpo)) — corpo vazio também é assinado;</item>
/// <item>base string = <c>MÉTODO&amp;enc(url sem query)&amp;enc(parâmetros ordenados: query + oauth_*)</c>;</item>
/// <item><c>oauth_signature</c> = base64(RSA PKCS#1 v1.5 SHA-256 da base string), com a chave privada do projeto.</item>
/// </list>
/// Implementado aqui (sem o SDK da Mastercard) para não trazer dependência nova; os testes conferem com a chave pública.
/// </summary>
public sealed class MastercardOAuthSigner(string consumerKey, RSA signingKey, TimeProvider clock)
{
    public static MastercardOAuthSigner FromOptions(MastercardOptions options, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(options.ConsumerKey) || string.IsNullOrWhiteSpace(options.SigningKeyPath))
        {
            throw new InvalidOperationException(
                "Mastercard:ConsumerKey and Mastercard:SigningKeyPath are required when Payments:Provider is Mastercard.");
        }
        return new MastercardOAuthSigner(options.ConsumerKey, LoadKey(options), clock);
    }

    private static RSA LoadKey(MastercardOptions options)
    {
        if (options.SigningKeyPath.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(options.SigningKeyPath));
            return rsa;
        }
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            options.SigningKeyPath, options.SigningKeyPassword, X509KeyStorageFlags.EphemeralKeySet);
        return certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The Mastercard signing key has no RSA private key.");
    }

    /// <summary>Valor do header <c>Authorization</c> para a requisição.</summary>
    public string CreateAuthorizationHeader(Uri uri, string method, string? body, string? nonce = null)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_body_hash"] = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty))),
            ["oauth_consumer_key"] = consumerKey,
            ["oauth_nonce"] = nonce ?? NewNonce(),
            ["oauth_signature_method"] = "RSA-SHA256",
            ["oauth_timestamp"] = clock.GetUtcNow().ToUnixTimeSeconds().ToString(),
            ["oauth_version"] = "1.0",
        };

        var baseString = BuildBaseString(uri, method, parameters);
        var signature = signingKey.SignData(Encoding.UTF8.GetBytes(baseString), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        parameters["oauth_signature"] = Convert.ToBase64String(signature);

        return "OAuth " + string.Join(",", parameters.Select(p => $"{p.Key}=\"{Encode(p.Value)}\""));
    }

    /// <summary>Texto que é assinado (exposto para os testes).</summary>
    public static string BuildBaseString(Uri uri, string method, IReadOnlyDictionary<string, string> oauthParameters)
    {
        var all = new List<KeyValuePair<string, string>>(oauthParameters);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
            all.Add(new(key, value));
        }

        var normalized = string.Join("&", all
            .Select(p => (Key: Encode(p.Key), Value: Encode(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));

        return $"{method.ToUpperInvariant()}&{Encode(BaseUri(uri))}&{Encode(normalized)}";
    }

    private static string BaseUri(Uri uri)
    {
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{port}{uri.AbsolutePath}";
    }

    /// <summary>Percent-encoding do RFC 3986 (o que o OAuth 1.0a exige).</summary>
    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}

/// <summary>Assina toda requisição que passa pelo HttpClient da Mastercard.</summary>
public sealed class MastercardOAuthHandler(MastercardOAuthSigner signer) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        request.Headers.TryAddWithoutValidation("Authorization", signer.CreateAuthorizationHeader(request.RequestUri!, request.Method.Method, body));
        return await base.SendAsync(request, cancellationToken);
    }
}
