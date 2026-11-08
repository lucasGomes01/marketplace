using System.Security.Cryptography;
using Payments.Infrastructure.Security;

namespace Payments.UnitTests.Infrastructure;

public class AesGcmCardProtectorTests
{
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Round_trips_without_the_number_appearing_in_the_stored_text()
    {
        var protector = new AesGcmCardProtector(Key);

        var stored = protector.Protect("5555555555554444");

        Assert.StartsWith("v1:", stored);
        Assert.DoesNotContain("5555555555554444", stored);
        Assert.Equal("5555555555554444", protector.Unprotect(stored));
    }

    [Fact]
    public void Same_number_encrypts_differently_each_time()
    {
        var protector = new AesGcmCardProtector(Key);

        Assert.NotEqual(protector.Protect("5555555555554444"), protector.Protect("5555555555554444"));
    }

    [Fact]
    public void Tampered_or_foreign_key_data_is_rejected()
    {
        var stored = new AesGcmCardProtector(Key).Protect("5555555555554444");
        var otherKey = new AesGcmCardProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        Assert.ThrowsAny<CryptographicException>(() => otherKey.Unprotect(stored));
    }

    [Theory]
    [InlineData("not-base64!")]
    [InlineData("c2hvcnQ=")]
    public void Key_must_be_32_bytes_in_base64(string key)
    {
        Assert.Throws<InvalidOperationException>(() => new AesGcmCardProtector(key));
    }
}
