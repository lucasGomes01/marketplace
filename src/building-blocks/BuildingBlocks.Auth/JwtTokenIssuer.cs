using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Auth;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Emite tokens. Só o serviço Users usa, mas mora aqui para usar os mesmos parâmetros da validação.</summary>
public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>Emite o token com um claim <c>role</c> para cada papel ativo.</summary>
    public AccessToken Issue(Guid userId, string email, IEnumerable<string> roles)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(jwt.ExpirationMinutes);

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(MarketplaceClaims.UserId, userId.ToString()),
                new Claim(MarketplaceClaims.Email, email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                .. roles.Select(role => new Claim(MarketplaceClaims.Role, role)),
            ]),
            SigningCredentials = new SigningCredentials(JwtValidation.CreateSigningKey(jwt), JwtValidation.Algorithm),
        });

        return new AccessToken(token, expiresAt);
    }
}
