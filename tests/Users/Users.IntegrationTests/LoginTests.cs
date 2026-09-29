using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Users.IntegrationTests;

[Collection(UsersApiCollection.Name)]
public class LoginTests(UsersApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string Email, string Password)> RegisterUser(string role = "Buyer")
    {
        var email = $"login-{Guid.NewGuid():N}@example.com";
        const string password = "Sup3r-secret";
        var response = await _client.PostAsJsonAsync("/users", new { email, name = "Maria", password, role });
        response.EnsureSuccessStatusCode();
        return (email, password);
    }

    private sealed record MeResponse(Guid Id, string Email, string Name, string Role);

    [Fact]
    public async Task Login_with_valid_credentials_returns_bearer_token_with_user_claims()
    {
        var (email, password) = await RegisterUser(role: "Seller");

        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UsersApiFactory.TokenResponse>();
        Assert.NotNull(body);
        Assert.Equal("Bearer", body.TokenType);
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(59));

        var jwt = new JsonWebToken(body.AccessToken);
        Assert.Equal(UsersApiFactory.JwtIssuer, jwt.Issuer);
        Assert.Equal(email, jwt.GetClaim("email").Value);
        Assert.Equal("Seller", jwt.GetClaim("role").Value);
        Assert.True(Guid.TryParse(jwt.Subject, out _));
    }

    [Fact]
    public async Task Login_normalizes_email()
    {
        var (email, password) = await RegisterUser();

        var response = await _client.PostAsJsonAsync("/auth/login", new { email = $"  {email.ToUpperInvariant()} ", password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_return_the_same_401()
    {
        var (email, _) = await RegisterUser();

        var wrongPassword = await _client.PostAsJsonAsync("/auth/login", new { email, password = "Wrong-passw0rd" });
        var unknownEmail = await _client.PostAsJsonAsync("/auth/login", new { email = "nobody@example.com", password = "Wrong-passw0rd" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await Title(wrongPassword), await Title(unknownEmail));
    }

    [Theory]
    [InlineData("", "Sup3r-secret")]
    [InlineData("maria@example.com", "")]
    [InlineData("   ", "   ")]
    public async Task Blank_email_or_password_returns_400(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_authenticated_user()
    {
        var (client, userId, email, _) = await factory.CreateAuthenticatedClient(role: "Seller");

        var response = await client.GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal(new MeResponse(userId, email, "Test User", "Seller"), me);
    }

    [Fact]
    public async Task Me_without_token_returns_401()
    {
        var response = await _client.GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_tampered_token_returns_401()
    {
        var (client, _, _, _) = await factory.CreateAuthenticatedClient();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        var response = await client.GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())?.Title;
}
