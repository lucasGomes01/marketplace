using BuildingBlocks.Application;
using Microsoft.Extensions.Time.Testing;
using Users.Application.Login;
using Users.Domain;

namespace Users.UnitTests.Application;

public class LoginHandlerTests
{
    private readonly InMemoryUserRepository _repository = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly FakeTokenIssuer _tokens;
    private readonly LoginHandler _handler;
    private readonly User _maria;

    public LoginHandlerTests()
    {
        _tokens = new FakeTokenIssuer(_clock);
        _handler = new LoginHandler(_repository, _hasher, _tokens);
        _maria = User.Register("maria@example.com", "Maria", _hasher.Hash("Sup3r-secret"), _clock);
        _maria.BecomeSeller(_clock);
        _repository.Users.Add(_maria);
    }

    private Task<Result<LoginResponse>> Login(string? email, string? password) =>
        _handler.HandleAsync(new LoginCommand(email, password));

    [Fact]
    public async Task Valid_credentials_issue_a_token_for_the_user()
    {
        var result = await Login(" MARIA@example.com ", "Sup3r-secret");

        Assert.Equal($"token-for-{_maria.Id}", result.Value.AccessToken);
        Assert.Equal(_clock.GetUtcNow().AddHours(1), result.Value.ExpiresAt);
        var issued = _tokens.LastIssued!.Value;
        Assert.Equal((_maria.Id, "maria@example.com"), (issued.UserId, issued.Email));
        Assert.Equal([UserRole.Buyer, UserRole.Seller], issued.Roles);
    }

    [Fact]
    public async Task Wrong_password_is_unauthorized()
    {
        var result = await Login("maria@example.com", "wrong-password");

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Unauthorized, error.Type);
        Assert.Equal("auth.invalid_credentials", error.Code);
    }

    [Fact]
    public async Task Unknown_email_gives_the_same_error_and_still_verifies_a_hash()
    {
        var wrongPassword = await Login("maria@example.com", "wrong-password");
        var unknownEmail = await Login("nobody@example.com", "wrong-password");

        Assert.Equal(Assert.Single(wrongPassword.Errors), Assert.Single(unknownEmail.Errors));
        // Mesmo custo nos dois casos: o tempo de resposta não revela se o email existe.
        Assert.Equal(2, _hasher.VerifyCalls);
    }

    [Fact]
    public async Task Malformed_email_is_unauthorized_not_a_validation_error()
    {
        var result = await Login("not-an-email", "Sup3r-secret");

        Assert.Equal(ErrorType.Unauthorized, Assert.Single(result.Errors).Type);
    }

    [Fact]
    public async Task Blank_fields_are_validation_errors()
    {
        var result = await Login("  ", "");

        Assert.Equal(["email.required", "password.required"], result.Errors.Select(e => e.Code));
        Assert.Null(_tokens.LastIssued);
    }
}
