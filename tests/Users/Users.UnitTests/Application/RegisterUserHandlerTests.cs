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
        string? email = "Maria@Example.com", string? name = "Maria", string? password = "Sup3r-secret", string? role = "seller") =>
        _handler.HandleAsync(new RegisterUserCommand(email, name, password, role));

    [Fact]
    public async Task Registers_user_with_hashed_password()
    {
        var result = await Register();

        Assert.True(result.IsSuccess);
        Assert.Equal("maria@example.com", result.Value.Email);
        Assert.Equal(UserRole.Seller, result.Value.Role);
        var stored = Assert.Single(_repository.Users);
        Assert.Equal(result.Value.Id, stored.Id);
        Assert.Equal("hashed:Sup3r-secret", stored.PasswordHash);
    }

    [Fact]
    public async Task Short_password_and_invalid_role_are_reported_together()
    {
        var result = await Register(password: "short", role: "Admin");

        Assert.False(result.IsSuccess);
        Assert.Equal(["password.too_short", "role.invalid"], result.Errors.Select(e => e.Code));
        Assert.All(result.Errors, e => Assert.Equal(ErrorType.Validation, e.Type));
        Assert.Empty(_repository.Users);
    }

    [Fact]
    public async Task Numeric_role_is_not_accepted()
    {
        var result = await Register(role: "1");

        Assert.Equal("role", Assert.Single(result.Errors).Field);
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
