using Users.Domain;

namespace Users.Application.Abstractions;

/// <summary>Porta de persistência de usuários (implementada com EF Core na Infrastructure).</summary>
public interface IUserRepository
{
    Task<bool> ExistsWithEmailAsync(Email email, CancellationToken cancellationToken);

    Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Grava alterações de um usuário já existente.</summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken);

    /// <summary>Grava o usuário.</summary>
    /// <exception cref="DuplicateEmailException">Outro usuário com o mesmo email foi gravado antes (corrida).</exception>
    Task AddAsync(User user, CancellationToken cancellationToken);
}

/// <summary>O armazenamento recusou o email por já existir (garantia final do índice único).</summary>
public sealed class DuplicateEmailException(Email email)
    : Exception($"A user with email '{email}' already exists.");
