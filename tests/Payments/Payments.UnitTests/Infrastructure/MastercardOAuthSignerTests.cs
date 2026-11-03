using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Payments.Infrastructure.Mastercard;

namespace Payments.UnitTests.Infrastructure;

public class MastercardOAuthSignerTests
{
    private readonly RSA _key = RSA.Create(2048);
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.FromUnixTimeSeconds(1_762_171_200));

    private static Dictionary<string, string> ParseHeader(string header)
    {
        Assert.StartsWith("OAuth ", header);
        return header["OAuth ".Length..].Split(',')
            .Select(part => part.Split('=', 2))
            .ToDictionary(kv => kv[0], kv => Uri.UnescapeDataString(kv[1].Trim('"')));
    }

    [Fact]
    public void Header_has_every_oauth_parameter_and_the_body_hash()
    {
        var signer = new MastercardOAuthSigner("consumer-key!123", _key, _clock);

        var header = ParseHeader(signer.CreateAuthorizationHeader(new Uri("https://sandbox.api.mastercard.com/x"), "POST", "{\"a\":1}"));

        Assert.Equal("consumer-key!123", header["oauth_consumer_key"]);
        Assert.Equal("RSA-SHA256", header["oauth_signature_method"]);
        Assert.Equal("1.0", header["oauth_version"]);
        Assert.Equal("1762171200", header["oauth_timestamp"]);
        Assert.Equal(Convert.ToBase64String(SHA256.HashData("{\"a\":1}"u8.ToArray())), header["oauth_body_hash"]);
        Assert.False(string.IsNullOrEmpty(header["oauth_nonce"]));
    }

    [Fact]
    public void Signature_verifies_with_the_public_key_over_the_base_string()
    {
        var signer = new MastercardOAuthSigner("ck", _key, _clock);
        var uri = new Uri("https://sandbox.api.mastercard.com/settlement/currencyrate/conversion-rate?fxDate=0000-00-00&transCurr=BRL&crdhldBillCurr=USD");

        var header = ParseHeader(signer.CreateAuthorizationHeader(uri, "GET", null, nonce: "abc"));

        var oauth = header.Where(h => h.Key != "oauth_signature").ToDictionary();
        var baseString = MastercardOAuthSigner.BuildBaseString(uri, "GET", oauth);
        Assert.True(_key.VerifyData(Encoding.UTF8.GetBytes(baseString), Convert.FromBase64String(header["oauth_signature"]),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Base_string_sorts_query_and_oauth_parameters_and_encodes_them()
    {
        var uri = new Uri("HTTPS://Sandbox.API.mastercard.com/send/v1/x?b=2&a=hello%20world&a=1");
        var oauth = new Dictionary<string, string> { ["oauth_nonce"] = "n", ["oauth_body_hash"] = "h+/=" };

        var baseString = MastercardOAuthSigner.BuildBaseString(uri, "post", oauth);

        Assert.Equal(
            "POST&https%3A%2F%2Fsandbox.api.mastercard.com%2Fsend%2Fv1%2Fx&" +
            "a%3D1%26a%3Dhello%2520world%26b%3D2%26oauth_body_hash%3Dh%252B%252F%253D%26oauth_nonce%3Dn",
            baseString);
    }

    [Fact]
    public async Task Handler_signs_the_outgoing_request()
    {
        var stub = new StubHttpHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        using var http = new HttpClient(new MastercardOAuthHandler(new MastercardOAuthSigner("ck", _key, _clock)) { InnerHandler = stub });

        await http.PostAsync("https://sandbox.api.mastercard.com/x", new StringContent("{}"));

        var auth = stub.Requests.Single().Request.Headers.GetValues("Authorization").Single();
        Assert.Contains("oauth_signature=", auth);
        Assert.Contains($"oauth_body_hash=\"{Uri.EscapeDataString(Convert.ToBase64String(SHA256.HashData("{}"u8.ToArray())))}\"", auth);
    }
}
