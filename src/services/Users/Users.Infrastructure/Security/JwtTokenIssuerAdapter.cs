using BuildingBlocks.Auth;
using Users.Application.Abstractions;
using Users.Domain;

namespace Users.Infrastructure.Security;

/// <summary>Adapta o emissor JWT compartilhado (BuildingBlocks.Auth) à porta da aplicação.</summary>
internal sealed class JwtTokenIssuerAdapter(JwtTokenIssuer issuer) : ITokenIssuer
{
    public IssuedToken Issue(Guid userId, string email, IReadOnlyCollection<UserRole> roles)
    {
        var token = issuer.Issue(userId, email, roles.Select(role => role.ToString()));
        return new IssuedToken(token.Value, token.ExpiresAt);
    }
}
