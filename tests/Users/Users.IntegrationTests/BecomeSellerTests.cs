using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Users.IntegrationTests;

[Collection(UsersApiCollection.Name)]
public class BecomeSellerTests(UsersApiFactory factory)
{
    private sealed record UserBody(Guid Id, string Email, string Name, string[] Roles, DateTimeOffset? SellerSince);

    private sealed record BecomeSellerBody(UserBody User, string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await factory.CreateClient().PostAsync("/users/me/seller", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Activates_the_seller_profile_and_returns_a_token_with_both_roles()
    {
        var (client, userId, _, _) = await factory.CreateAuthenticatedClient();

        var response = await client.PostAsync("/users/me/seller", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BecomeSellerBody>();
        Assert.NotNull(body);
        Assert.Equal(userId, body.User.Id);
        Assert.Equal(["Buyer", "Seller"], body.User.Roles);
        Assert.NotNull(body.User.SellerSince);
        Assert.Equal("Bearer", body.TokenType);
        var roles = new JsonWebToken(body.AccessToken).Claims.Where(c => c.Type == "role").Select(c => c.Value);
        Assert.Equal(["Buyer", "Seller"], roles);
    }

    [Fact]
    public async Task The_change_is_persisted()
    {
        var (client, _, _, _) = await factory.CreateAuthenticatedClient();
        var activated = await (await client.PostAsync("/users/me/seller", null)).Content.ReadFromJsonAsync<BecomeSellerBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", activated!.AccessToken);

        var me = await client.GetFromJsonAsync<UserBody>("/users/me");

        Assert.Equal(["Buyer", "Seller"], me!.Roles);
        Assert.Equal(activated.User.SellerSince, me.SellerSince);
    }

    [Fact]
    public async Task Activating_twice_keeps_the_original_date()
    {
        var (client, _, _, _) = await factory.CreateAuthenticatedClient();
        var first = await (await client.PostAsync("/users/me/seller", null)).Content.ReadFromJsonAsync<BecomeSellerBody>();

        var second = await (await client.PostAsync("/users/me/seller", null)).Content.ReadFromJsonAsync<BecomeSellerBody>();

        Assert.Equal(first!.User.SellerSince, second!.User.SellerSince);
    }
}
