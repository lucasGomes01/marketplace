using System.Net;

namespace Cart.IntegrationTests;

[Collection(CartApiCollection.Name)]
public class HealthEndpointTests(CartApiFactory factory)
{
    [Fact]
    public async Task Get_health_returns_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
