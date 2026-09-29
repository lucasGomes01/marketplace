using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BuildingBlocks.Auth.UnitTests;

public class JwtTokenIssuerTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static JwtOptions Options(string key = "test-signing-key-with-at-least-32-chars!", string issuer = "marketplace-users") => new()
    {
        Issuer = issuer,
        Audience = "marketplace",
        SigningKey = key,
        ExpirationMinutes = 60,
    };

    private static AccessToken Issue(JwtOptions? options = null, DateTimeOffset? now = null) =>
        new JwtTokenIssuer(Microsoft.Extensions.Options.Options.Create(options ?? Options()),
                new FakeTimeProvider(now ?? DateTimeOffset.UtcNow))
            .Issue(UserId, "maria@example.com", "Seller");

    private static Task<Microsoft.IdentityModel.Tokens.TokenValidationResult> Validate(AccessToken token, JwtOptions? options = null) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token.Value, JwtValidation.CreateParameters(options ?? Options()));

    [Fact]
    public async Task Issued_token_is_valid_and_carries_user_claims()
    {
        var result = await Validate(Issue());

        Assert.True(result.IsValid, result.Exception?.Message);
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        Assert.Equal(UserId, principal.GetUserId());
        Assert.Equal("maria@example.com", principal.FindFirstValue(MarketplaceClaims.Email));
        Assert.True(principal.IsInRole("Seller"));
    }

    [Fact]
    public void Token_expires_after_configured_minutes()
    {
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        var token = Issue(now: now);

        Assert.Equal(now.AddMinutes(60), token.ExpiresAt);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = Issue(now: DateTimeOffset.UtcNow.AddHours(-2));

        var result = await Validate(token);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var token = Issue(Options(key: "another-signing-key-with-32-chars-or-more"));

        var result = await Validate(token);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Token_from_another_issuer_is_rejected()
    {
        var token = Issue(Options(issuer: "someone-else"));

        var result = await Validate(token);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("short-key")]
    [InlineData("")]
    public void Signing_key_shorter_than_32_chars_is_invalid(string key)
    {
        var options = Options(key: key);

        var valid = Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);

        Assert.False(valid);
    }

    [Fact]
    public void Get_user_id_returns_null_without_sub_claim()
    {
        Assert.Null(new ClaimsPrincipal(new ClaimsIdentity()).GetUserId());
    }
}
