namespace Users.Application.Abstractions;

/// <summary>
/// Porta de hashing de senha: a aplicação pede o hash, a Infrastructure decide o algoritmo (PBKDF2 do Identity).
/// O domínio nunca vê a senha em texto puro.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
