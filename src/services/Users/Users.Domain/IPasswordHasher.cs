namespace Users.Domain;

/// <summary>
/// Hashing de senha fica fora da entidade: o domínio só recebe e guarda o hash.
/// A implementação (ASP.NET Identity PasswordHasher) mora na camada Api.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
