using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Users.Domain;

namespace Users.IntegrationTests;

[Collection(UsersApiCollection.Name)]
public class RegisterUserTests(UsersApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object ValidRequest(string? email = null, string name = "Maria Silva", string password = "Sup3r-secret") => new
    {
        email = email ?? $"user-{Guid.NewGuid():N}@example.com",
        name,
        password,
    };

    private record UserResponse(Guid Id, string Email, string Name, string[] Roles, DateTimeOffset? SellerSince);

    [Fact]
    public async Task Register_returns_201_with_a_buyer_account_and_location()
    {
        var response = await _client.PostAsJsonAsync("/users", ValidRequest(email: " Nova.Pessoa@Example.com "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("nova.pessoa@example.com", body.Email);
        Assert.Equal("Maria Silva", body.Name);
        Assert.Equal(["Buyer"], body.Roles);
        Assert.Null(body.SellerSince);
        Assert.Equal($"/users/{body.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Password_is_stored_hashed_and_never_returned()
    {
        const string password = "Sup3r-secret";

        var response = await _client.PostAsJsonAsync("/users", ValidRequest(password: password));

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);

        var id = (await response.Content.ReadFromJsonAsync<UserResponse>())!.Id;
        var stored = await factory.WithDbContext(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == id));
        Assert.NotEqual(password, stored.PasswordHash);
        Assert.True(new Infrastructure.Security.IdentityPasswordHasher().Verify(stored.PasswordHash, password));
    }

    [Fact]
    public async Task Registering_an_existing_email_returns_409()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await _client.PostAsJsonAsync("/users", ValidRequest(email: email));

        var response = await _client.PostAsJsonAsync("/users", ValidRequest(email: email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(409, problem?.Status);
    }

    [Theory]
    [InlineData("not-an-email", "Maria", "Sup3r-secret")]
    [InlineData(null, "   ", "Sup3r-secret")]
    [InlineData(null, "Maria", "short")]
    public async Task Invalid_data_returns_400_validation_problem(string? email, string name, string password)
    {
        var response = await _client.PostAsJsonAsync("/users", ValidRequest(email, name, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task Email_has_unique_index_in_database()
    {
        var index = await factory.WithDbContext(db => Task.FromResult(
            db.Model.FindEntityType(typeof(User))!.GetIndexes()
                .Single(i => i.Properties.Any(p => p.Name == nameof(User.Email)))));

        Assert.True(index.IsUnique);
    }
}
