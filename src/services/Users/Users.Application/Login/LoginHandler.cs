using BuildingBlocks.Application;
using Users.Application.Abstractions;
using Users.Domain;

namespace Users.Application.Login;

public sealed record LoginCommand(string? Email, string? Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class LoginHandler(IUserRepository users, IPasswordHasher hasher, ITokenIssuer tokens)
    : IHandler<LoginCommand, LoginResponse>
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Invalid email or password.");

    // Hash de uma senha aleatória: com email inexistente, verificamos contra ele para gastar o mesmo tempo de um
    // login real e não revelar, pelo tempo de resposta, quais emails estão cadastrados.
    private static string? _dummyPasswordHash;

    public async Task<Result<LoginResponse>> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        var errors = new List<Error>();
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            errors.Add(Error.Validation("email", "email.required", "Email is required."));
        }
        if (string.IsNullOrWhiteSpace(command.Password))
        {
            errors.Add(Error.Validation("password", "password.required", "Password is required."));
        }
        if (errors.Count > 0)
        {
            return Result<LoginResponse>.Failure(errors);
        }

        var user = await FindByEmail(command.Email!, cancellationToken);
        _dummyPasswordHash ??= hasher.Hash(Guid.NewGuid().ToString());

        var passwordMatches = hasher.Verify(user?.PasswordHash ?? _dummyPasswordHash, command.Password!);
        if (user is null || !passwordMatches)
        {
            return InvalidCredentials;
        }

        var token = tokens.Issue(user.Id, user.Email.Value, user.Role);
        return new LoginResponse(token.Value, token.ExpiresAt);
    }

    private async Task<User?> FindByEmail(string rawEmail, CancellationToken cancellationToken)
    {
        Email email;
        try
        {
            email = Email.Create(rawEmail);
        }
        catch (DomainException)
        {
            // Formato inválido é tratado como credencial inválida, sem dar pistas.
            return null;
        }

        return await users.FindByEmailAsync(email, cancellationToken);
    }
}
