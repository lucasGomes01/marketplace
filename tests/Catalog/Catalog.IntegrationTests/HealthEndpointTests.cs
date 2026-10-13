using System.Net;

namespace Catalog.IntegrationTests;

[Collection(CatalogApiCollection.Name)]
public class HealthEndpointTests(CatalogApiFactory factory)
{
    [Fact]
    public async Task Get_health_returns_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
