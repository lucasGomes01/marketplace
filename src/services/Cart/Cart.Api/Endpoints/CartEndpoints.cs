using System.Security.Claims;
using BuildingBlocks.Api;
using BuildingBlocks.Application;
using BuildingBlocks.Auth;
using Cart.Application;
using Cart.Application.AddItem;
using Cart.Application.ChangeQuantity;
using Cart.Application.Checkout;
using Cart.Application.GetCart;
using Cart.Application.RemoveItem;

namespace Cart.Api.Endpoints;

/// <summary>Só tradução HTTP ↔ caso de uso. O comprador é sempre quem está autenticado.</summary>
public static class CartEndpoints
{
    /// <summary>Sem preço/nome: esses dados vêm do Catalog.</summary>
    public sealed record AddItemRequest(Guid? ProductId, int? Quantity);

    public sealed record ChangeQuantityRequest(int? Quantity);

    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/cart").RequireAuthorization(MarketplacePolicies.Buyer);

        cart.MapGet("/", GetAsync).WithName("GetCart").Produces<CartResponse>();

        cart.MapPost("/items", AddAsync)
            .WithName("AddCartItem")
            .Produces<CartResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        cart.MapPut("/items/{productId:guid}", ChangeQuantityAsync)
            .WithName("ChangeCartItemQuantity")
            .Produces<CartResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        cart.MapPost("/checkout", CheckoutAsync)
            .WithName("Checkout")
            .Produces<CheckoutResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        cart.MapDelete("/items/{productId:guid}", RemoveAsync)
            .WithName("RemoveCartItem")
            .Produces<CartResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static Task<IResult> GetAsync(ClaimsPrincipal user, IHandler<GetCartQuery, CartResponse> handler, CancellationToken ct) =>
        Run(user, buyerId => handler.HandleAsync(new GetCartQuery(buyerId), ct));

    private static Task<IResult> AddAsync(
        AddItemRequest request, ClaimsPrincipal user, IHandler<AddCartItemCommand, CartResponse> handler, CancellationToken ct) =>
        Run(user, buyerId => handler.HandleAsync(new AddCartItemCommand(buyerId, request.ProductId, request.Quantity), ct));

    private static Task<IResult> ChangeQuantityAsync(
        Guid productId, ChangeQuantityRequest request, ClaimsPrincipal user,
        IHandler<ChangeCartItemQuantityCommand, CartResponse> handler, CancellationToken ct) =>
        Run(user, buyerId => handler.HandleAsync(new ChangeCartItemQuantityCommand(buyerId, productId, request.Quantity), ct));

    private static Task<IResult> RemoveAsync(
        Guid productId, ClaimsPrincipal user, IHandler<RemoveCartItemCommand, CartResponse> handler, CancellationToken ct) =>
        Run(user, buyerId => handler.HandleAsync(new RemoveCartItemCommand(buyerId, productId), ct));

    /// <summary>202: o pedido é criado de forma assíncrona pelo Orders (acompanhe em <c>/api/orders/{orderId}</c>).</summary>
    private static async Task<IResult> CheckoutAsync(
        ClaimsPrincipal user, IHandler<CheckoutCommand, CheckoutResponse> handler, CancellationToken ct)
    {
        if (user.GetUserId() is not { } buyerId)
        {
            return Results.Unauthorized();
        }
        var result = await handler.HandleAsync(new CheckoutCommand(buyerId), ct);
        return result.ToHttpResult(checkout => Results.Accepted($"/orders/{checkout.OrderId}", checkout));
    }

    private static async Task<IResult> Run(ClaimsPrincipal user, Func<Guid, Task<Result<CartResponse>>> useCase)
    {
        if (user.GetUserId() is not { } buyerId)
        {
            return Results.Unauthorized();
        }
        var result = await useCase(buyerId);
        return result.ToHttpResult(cart => Results.Ok(cart));
    }
}
