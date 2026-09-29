using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Auth;

public static class JwtValidation
{
    public const string Algorithm = SecurityAlgorithms.HmacSha256;

    public static SymmetricSecurityKey CreateSigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));

    public static TokenValidationParameters CreateParameters(JwtOptions options) => new()
    {
        ValidIssuer = options.Issuer,
        ValidAudience = options.Audience,
        IssuerSigningKey = CreateSigningKey(options),
        ValidAlgorithms = [Algorithm],
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = MarketplaceClaims.UserId,
        RoleClaimType = MarketplaceClaims.Role,
    };
}
