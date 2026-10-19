using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.IntegrationTests;

/// <summary>
/// Servidor "eco" que responde com o caminho recebido. Cada cluster do gateway aponta para um prefixo diferente
/// (ex.: <c>/svc-users/</c>), então a resposta revela qual serviço recebeu a chamada e com qual caminho.
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string[] Clusters = ["users", "catalog", "cart", "orders"];
    private WebApplication? _echo;
    private string _echoUrl = string.Empty;
    private int _hits;

    async Task IAsyncLifetime.InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _echo = builder.Build();
        _echo.Run(ctx =>
        {
            // Conta as chamadas que chegam ao serviço: resposta vinda do cache do gateway repete o número.
            ctx.Response.Headers["X-Upstream-Hit"] = Interlocked.Increment(ref _hits).ToString();
            return ctx.Response.WriteAsync($"{ctx.Request.Path}{ctx.Request.QueryString}");
        });
        await _echo.StartAsync();
        _echoUrl = _echo.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var cluster in Clusters)
        {
            builder.UseSetting($"ReverseProxy:Clusters:{cluster}:Destinations:d1:Address", $"{_echoUrl}/svc-{cluster}/");
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        if (_echo is not null)
        {
            await _echo.DisposeAsync();
        }
    }
}

public class GatewayRoutingTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/api/users", "/svc-users/users")]
    [InlineData("/api/users/me", "/svc-users/users/me")]
    [InlineData("/api/auth/login", "/svc-users/auth/login")]
    [InlineData("/api/products", "/svc-catalog/products")]
    [InlineData("/api/products/123?page=2&size=10", "/svc-catalog/products/123?page=2&size=10")]
    [InlineData("/api/cart/items", "/svc-cart/cart/items")]
    [InlineData("/api/orders", "/svc-orders/orders")]
    [InlineData("/api/orders/o-1/shipments/s-1/delivery", "/svc-orders/orders/o-1/shipments/s-1/delivery")]
    [InlineData("/api/shipments", "/svc-orders/shipments")]
    [InlineData("/api/shipments/o-1/status", "/svc-orders/shipments/o-1/status")]
    [InlineData("/health/users", "/svc-users/health")]
    [InlineData("/health/catalog", "/svc-catalog/health")]
    [InlineData("/health/cart", "/svc-cart/health")]
    [InlineData("/health/orders", "/svc-orders/health")]
    public async Task Routes_resource_to_the_right_service_and_path(string publicPath, string expectedUpstream)
    {
        var response = await _client.GetAsync(publicPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedUpstream, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        var response = await _client.GetAsync("/api/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_answers_its_own_health()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gateway", await response.Content.ReadAsStringAsync());
    }
}
