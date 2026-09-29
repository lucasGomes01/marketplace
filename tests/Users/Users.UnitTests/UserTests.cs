using Microsoft.Extensions.Time.Testing;
using Users.Domain;

namespace Users.UnitTests;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Now);

    private User Register(
        string email = "maria@example.com",
        string name = "Maria Silva",
        UserRole role = UserRole.Buyer,
        string passwordHash = "hashed-password") =>
        User.Register(email, name, role, passwordHash, _clock);

    [Fact]
    public void Register_creates_user_with_given_data()
    {
        var user = Register(email: " Maria@Example.com ", name: "  Maria Silva ", role: UserRole.Seller);

        Assert.Equal("maria@example.com", user.Email.Value);
        Assert.Equal("Maria Silva", user.Name);
        Assert.Equal(UserRole.Seller, user.Role);
        Assert.Equal("hashed-password", user.PasswordHash);
    }

    [Fact]
    public void Register_assigns_version_7_id_and_creation_time()
    {
        var user = Register();

        Assert.Equal(7, user.Id.Version);
        Assert.Equal(Now, user.CreatedAt);
    }

    [Fact]
    public void Each_registered_user_gets_a_different_id()
    {
        Assert.NotEqual(Register().Id, Register().Id);
    }

    [Fact]
    public void Register_with_invalid_email_throws_domain_exception()
    {
        Assert.Throws<DomainException>(() => Register(email: "not-an-email"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_with_blank_name_throws_domain_exception(string name)
    {
        Assert.Throws<DomainException>(() => Register(name: name));
    }

    [Fact]
    public void Register_with_name_longer_than_100_chars_throws_domain_exception()
    {
        Assert.Throws<DomainException>(() => Register(name: new string('a', 101)));
    }

    [Fact]
    public void Register_with_undefined_role_throws_domain_exception()
    {
        Assert.Throws<DomainException>(() => Register(role: (UserRole)99));
    }

    [Fact]
    public void Domain_exception_identifies_the_invalid_field()
    {
        Assert.Equal("email", Assert.Throws<DomainException>(() => Register(email: "x")).Field);
        Assert.Equal("name", Assert.Throws<DomainException>(() => Register(name: "")).Field);
        Assert.Equal("role", Assert.Throws<DomainException>(() => Register(role: (UserRole)99)).Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_without_password_hash_throws_domain_exception(string passwordHash)
    {
        Assert.Throws<DomainException>(() => Register(passwordHash: passwordHash));
    }
}
