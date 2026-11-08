using BuildingBlocks.Api;
using BuildingBlocks.Application;
using Payments.Application.ConvertPrice;

namespace Payments.Api.Endpoints;

public static class ExchangeRatesEndpoints
{
    public static IEndpointRouteBuilder MapExchangeRatesEndpoints(this IEndpointRouteBuilder app)
    {
        // Público: a vitrine mostra o preço convertido para quem não fez login.
        app.MapGet("/exchange-rates/convert", ConvertAsync)
            .WithName("ConvertPrice")
            .Produces<ConversionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<IResult> ConvertAsync(
        decimal? amount, string? from, string? to, IHandler<ConvertPriceQuery, ConversionResponse> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new ConvertPriceQuery(amount, from, to), ct);
        return result.ToHttpResult(conversion => Results.Ok(conversion));
    }
}
