using System.Net;
using System.Net.Http.Headers;

namespace Gateway.IntegrationTests;

/// <summary>
/// Leituras públicas do catálogo são guardadas por alguns segundos no gateway: a mesma requisição não chega ao
/// Catalog de novo. O resto (escrita, com login, outros serviços) sempre passa.
/// </summary>
public class GatewayCacheTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string UpstreamHit(HttpResponseMessage response) =>
        response.Headers.GetValues("X-Upstream-Hit").Single();

    private static string Unique(string path) => $"{path}{(path.Contains('?') ? '&' : '?')}t={Guid.NewGuid():N}";

    [Theory]
    [InlineData("/api/products?page=1&pageSize=12")]
    [InlineData("/api/products/0198a1b2-0000-7000-8000-000000000001")]
    public async Task Repeated_public_catalog_read_is_answered_from_the_cache(string path)
    {
        var url = Unique(path);

        var first = await _client.GetAsync(url);
        var second = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(UpstreamHit(first), UpstreamHit(second));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Different_query_is_a_different_cache_entry()
    {
        var token = Guid.NewGuid().ToString("N");

        var page1 = await _client.GetAsync($"/api/products?page=1&t={token}");
        var page2 = await _client.GetAsync($"/api/products?page=2&t={token}");

        Assert.NotEqual(UpstreamHit(page1), UpstreamHit(page2));
        Assert.EndsWith("page=2&t=" + token, await page2.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logged_in_reads_are_not_cached()
    {
        var url = Unique("/api/products");
        Task<HttpResponseMessage> Send()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token");
            return _client.SendAsync(request);
        }

        Assert.NotEqual(UpstreamHit(await Send()), UpstreamHit(await Send()));
    }

    [Fact]
    public async Task Writes_are_never_cached()
    {
        var url = Unique("/api/products");

        var first = await _client.PostAsync(url, new StringContent("{}"));
        var second = await _client.PostAsync(url, new StringContent("{}"));

        Assert.NotEqual(UpstreamHit(first), UpstreamHit(second));
    }

    [Theory]
    [InlineData("/api/cart")]
    [InlineData("/api/orders")]
    [InlineData("/health/catalog")]
    public async Task Other_routes_are_not_cached(string path)
    {
        var url = Unique(path);

        var first = await _client.GetAsync(url);
        var second = await _client.GetAsync(url);

        Assert.NotEqual(UpstreamHit(first), UpstreamHit(second));
    }
}
