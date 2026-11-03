using System.Security.Claims;
using BuildingBlocks.Api;
using BuildingBlocks.Application;
using BuildingBlocks.Auth;
using Payments.Application;
using Payments.Application.GetPayouts;
using Payments.Application.RegisterPayoutCard;

namespace Payments.Api.Endpoints;

/// <summary>Só tradução HTTP ↔ caso de uso: nenhuma regra de negócio aqui.</summary>
public static class PayoutsEndpoints
{
    /// <summary>Sem <c>sellerId</c>: o vendedor é sempre quem está autenticado.</summary>
    public sealed record RegisterCardRequest(string? CardNumber, int? ExpiryMonth, int? ExpiryYear, string? HolderName);

    public static IEndpointRouteBuilder MapPayoutsEndpoints(this IEndpointRouteBuilder app)
    {
        var payouts = app.MapGroup("/payouts").RequireAuthorization(MarketplacePolicies.Seller);

        payouts.MapGet("/", ListAsync)
            .WithName("GetSellerPayouts")
            .Produces<SellerPayoutsResponse>();

        payouts.MapPut("/card", RegisterCardAsync)
            .WithName("RegisterPayoutCard")
            .Produces<PayoutCardResponse>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal, IHandler<GetSellerPayoutsQuery, SellerPayoutsResponse> handler, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } sellerId)
        {
            return Results.Unauthorized();
        }
        var result = await handler.HandleAsync(new GetSellerPayoutsQuery(sellerId), ct);
        return result.ToHttpResult(payouts => Results.Ok(payouts));
    }

    private static async Task<IResult> RegisterCardAsync(
        RegisterCardRequest request, ClaimsPrincipal principal,
        IHandler<RegisterPayoutCardCommand, PayoutCardResponse> handler, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } sellerId)
        {
            return Results.Unauthorized();
        }
        var result = await handler.HandleAsync(new RegisterPayoutCardCommand(
            sellerId, request.CardNumber, request.ExpiryMonth, request.ExpiryYear, request.HolderName), ct);
        return result.ToHttpResult(card => Results.Ok(card));
    }
}
