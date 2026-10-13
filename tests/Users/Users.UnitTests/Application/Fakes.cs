using Users.Application.Abstractions;
using Users.Domain;

namespace Users.UnitTests.Application;

/// <summary>Repositório em memória: implementa a mesma porta que o repositório EF (Liskov).</summary>
internal sealed class InMemoryUserRepository : IUserRepository
{
    public List<User> Users { get; } = [];

    /// <summary>Simula outra requisição gravando o mesmo email entre a checagem e o insert.</summary>
    public bool SimulateDuplicateOnAdd { get; set; }

    public Task<bool> ExistsWithEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Any(u => u.Email == email));

    public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(u => u.Email == email));

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(u => u.Id == id));

    public int Updates { get; private set; }

    public Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        Updates++;
        return Task.CompletedTask;
    }

    public Task AddAsync(User user, CancellationToken cancellationToken)
    {
        if (SimulateDuplicateOnAdd)
        {
            throw new DuplicateEmailException(user.Email);
        }
        Users.Add(user);
        return Task.CompletedTask;
    }
}

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int VerifyCalls { get; private set; }

    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string passwordHash, string password)
    {
        VerifyCalls++;
        return passwordHash == Hash(password);
    }
}

internal sealed class FakeTokenIssuer(TimeProvider clock) : ITokenIssuer
{
    public (Guid UserId, string Email, UserRole[] Roles)? LastIssued { get; private set; }

    public IssuedToken Issue(Guid userId, string email, IReadOnlyCollection<UserRole> roles)
    {
        LastIssued = (userId, email, roles.ToArray());
        return new IssuedToken($"token-for-{userId}", clock.GetUtcNow().AddHours(1));
    }
}
