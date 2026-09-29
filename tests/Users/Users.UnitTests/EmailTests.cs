using Users.Domain;

namespace Users.UnitTests;

public class EmailTests
{
    [Fact]
    public void Email_is_trimmed_and_lowercased()
    {
        var email = Email.Create("  Maria.Silva@Example.COM ");

        Assert.Equal("maria.silva@example.com", email.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("maria")]
    [InlineData("maria@")]
    [InlineData("@example.com")]
    [InlineData("maria@example")]
    [InlineData("maria silva@example.com")]
    [InlineData("maria@@example.com")]
    public void Invalid_email_throws_domain_exception(string value)
    {
        Assert.Throws<DomainException>(() => Email.Create(value));
    }

    [Fact]
    public void Emails_with_same_normalized_value_are_equal()
    {
        Assert.Equal(Email.Create("A@B.com"), Email.Create("a@b.com"));
    }
}
