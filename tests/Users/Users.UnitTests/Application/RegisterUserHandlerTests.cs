using BuildingBlocks.Application;
using Microsoft.Extensions.Time.Testing;
using Users.Application.Register;
using Users.Domain;

namespace Users.UnitTests.Application;

public class RegisterUserHandlerTests
{
    private readonly InMemoryUserRepository _repository = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly RegisterUserHandler _handler;

    public RegisterUserHandlerTests() =>
        _handler = new RegisterUserHandler(_repository, _hasher, new FakeTimeProvider(DateTimeOffset.UtcNow));

    private Task<Result<Users.Application.UserResponse>> Register(
        string? email = "Maria@Example.com", string? name = "Maria", string? password = "Sup3r-secret") =>
        _handler.HandleAsync(new RegisterUserCommand(email, name, password));

    [Fact]
    public async Task Registers_user_with_hashed_password()
    {
        var result = await Register();

        Assert.True(result.IsSuccess);
        Assert.Equal("maria@example.com", result.Value.Email);
        Assert.Equal([UserRole.Buyer], result.Value.Roles);
        var stored = Assert.Single(_repository.Users);
        Assert.Equal(result.Value.Id, stored.Id);
        Assert.Equal("hashed:Sup3r-secret", stored.PasswordHash);
    }

    [Fact]
    public async Task Short_password_is_a_validation_error()
    {
        var result = await Register(password: "short");

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("password.too_short", error.Code);
        Assert.Empty(_repository.Users);
    }

    [Theory]
    [InlineData("not-an-email", null, "email")]
    [InlineData("maria@example.com", "   ", "name")]
    public async Task Domain_rule_violations_become_validation_errors_on_the_field(string email, string? name, string field)
    {
        var result = await Register(email: email, name: name ?? "Maria");

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(field, error.Field);
    }

    [Fact]
    public async Task Existing_email_is_a_conflict()
    {
        await Register(email: "maria@example.com");

        var result = await Register(email: "MARIA@example.com");

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("email.already_registered", error.Code);
        Assert.Single(_repository.Users);
    }

    [Fact]
    public async Task Duplicate_detected_by_the_database_is_also_a_conflict()
    {
        _repository.SimulateDuplicateOnAdd = true;

        var result = await Register();

        Assert.Equal("email.already_registered", Assert.Single(result.Errors).Code);
    }
}
