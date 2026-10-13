using Users.Domain;

namespace Users.Application.Abstractions;

public sealed record IssuedToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Porta de emissão de token de acesso (JWT na Infrastructure).</summary>
public interface ITokenIssuer
{
    IssuedToken Issue(Guid userId, string email, UserRole role);
}
