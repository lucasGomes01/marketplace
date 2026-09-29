using System.Net;

namespace Users.IntegrationTests;

[Collection(UsersApiCollection.Name)]
public class HealthEndpointTests(UsersApiFactory factory)
{
    [Fact]
    public async Task Get_health_returns_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
