using Microsoft.AspNetCore.Identity;

namespace Users.Api.Infrastructure;

/// <summary>PBKDF2 do ASP.NET Identity. O hash já inclui salt, algoritmo e número de iterações.</summary>
public sealed class IdentityPasswordHasher : Domain.IPasswordHasher
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object NoUser = new();

    public string Hash(string password) => Hasher.HashPassword(NoUser, password);

    public bool Verify(string passwordHash, string password) =>
        Hasher.VerifyHashedPassword(NoUser, passwordHash, password) != PasswordVerificationResult.Failed;
}
