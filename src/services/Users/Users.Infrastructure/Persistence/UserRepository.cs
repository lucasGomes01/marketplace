using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Users.Application.Abstractions;
using Users.Domain;

namespace Users.Infrastructure.Persistence;

internal sealed class UserRepository(UsersDbContext db) : IUserRepository
{
    // Violação de índice único no SQL Server.
    private static readonly int[] UniqueViolationErrors = [2601, 2627];

    public Task<bool> ExistsWithEmailAsync(Email email, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        db.Users.Update(user);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationErrors.Contains(sql.Number))
        {
            db.Entry(user).State = EntityState.Detached;
            throw new DuplicateEmailException(user.Email);
        }
    }
}
