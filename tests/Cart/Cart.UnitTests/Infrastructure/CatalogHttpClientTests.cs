using System.Net;
using System.Text;
using Cart.Application.Abstractions;
using Cart.Infrastructure.Catalog;

namespace Cart.UnitTests.Infrastructure;

public class CatalogHttpClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static (CatalogHttpClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://catalog.test/") };
        return (new CatalogHttpClient(http), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Reads_the_product_from_the_catalog_api()
    {
        var id = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var (client, handler) = Create(_ => Json($$"""
            {"id":"{{id}}","sellerId":"{{sellerId}}","name":"Furadeira","description":null,
             "price":199.90,"currency":"BRL","stock":5,"createdAt":"2026-10-13T12:00:00Z"}
            """));

        var product = await client.FindAsync(id, CancellationToken.None);

        Assert.Equal(new CatalogProduct(id, sellerId, "Furadeira", 199.90m, "BRL", 5), product);
        Assert.Equal($"http://catalog.test/products/{id}", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Not_found_means_null()
    {
        var (client, _) = Create(_ => Json("""{"status":404}""", HttpStatusCode.NotFound));

        Assert.Null(await client.FindAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Server_errors_mean_unavailable(HttpStatusCode status)
    {
        var (client, _) = Create(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => client.FindAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task Connection_failure_means_unavailable()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => client.FindAsync(Guid.CreateVersion7(), CancellationToken.None));
    }
}
