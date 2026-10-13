using BuildingBlocks.Application;
using Users.Application.Abstractions;
using Users.Domain;

namespace Users.Application.Register;

public sealed record RegisterUserCommand(string? Email, string? Name, string? Password, string? Role);

public sealed class RegisterUserHandler(IUserRepository users, IPasswordHasher hasher, TimeProvider clock)
    : IHandler<RegisterUserCommand, UserResponse>
{
    public const int PasswordMinLength = 8;

    private static readonly Error EmailAlreadyRegistered =
        Error.Conflict("email.already_registered", "Email already registered.");

    public async Task<Result<UserResponse>> HandleAsync(
        RegisterUserCommand command, CancellationToken cancellationToken = default)
    {
        // Regras da aplicação (a senha em texto puro nunca chega ao domínio).
        var errors = new List<Error>();
        if ((command.Password?.Length ?? 0) < PasswordMinLength)
        {
            errors.Add(Error.Validation("password", "password.too_short",
                $"Password must have at least {PasswordMinLength} characters."));
        }
        if (!Enum.GetNames<UserRole>().Contains(command.Role, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(Error.Validation("role", "role.invalid",
                $"Role must be one of: {string.Join(", ", Enum.GetNames<UserRole>())}."));
        }
        if (errors.Count > 0)
        {
            return Result<UserResponse>.Failure(errors);
        }

        User user;
        try
        {
            var role = Enum.Parse<UserRole>(command.Role!, ignoreCase: true);
            user = User.Register(command.Email!, command.Name!, role, hasher.Hash(command.Password!), clock);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Field, $"{ex.Field ?? "request"}.invalid", ex.Message);
        }

        if (await users.ExistsWithEmailAsync(user.Email, cancellationToken))
        {
            return EmailAlreadyRegistered;
        }

        try
        {
            await users.AddAsync(user, cancellationToken);
        }
        catch (DuplicateEmailException)
        {
            return EmailAlreadyRegistered;
        }

        return UserResponse.From(user);
    }
}
