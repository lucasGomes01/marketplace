using BuildingBlocks.Application;
using Users.Application.Abstractions;
using Users.Domain;

namespace Users.Application.Register;

/// <summary>Toda conta nasce compradora; vender é ativado depois (BecomeSeller).</summary>
public sealed record RegisterUserCommand(string? Email, string? Name, string? Password);

public sealed class RegisterUserHandler(IUserRepository users, IPasswordHasher hasher, TimeProvider clock)
    : IHandler<RegisterUserCommand, UserResponse>
{
    public const int PasswordMinLength = 8;

    private static readonly Error EmailAlreadyRegistered =
        Error.Conflict("email.already_registered", "Email already registered.");

    public async Task<Result<UserResponse>> HandleAsync(
        RegisterUserCommand command, CancellationToken cancellationToken = default)
    {
        // Regra da aplicação (a senha em texto puro nunca chega ao domínio).
        if ((command.Password?.Length ?? 0) < PasswordMinLength)
        {
            return Error.Validation("password", "password.too_short",
                $"Password must have at least {PasswordMinLength} characters.");
        }

        User user;
        try
        {
            user = User.Register(command.Email!, command.Name!, hasher.Hash(command.Password!), clock);
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
