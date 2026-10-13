using System.Security.Claims;
using BuildingBlocks.Api;
using BuildingBlocks.Application;
using BuildingBlocks.Auth;
using Users.Application;
using Users.Application.BecomeSeller;
using Users.Application.GetUser;
using Users.Application.Register;

namespace Users.Api.Endpoints;

/// <summary>Só tradução HTTP ↔ caso de uso: nenhuma regra de negócio aqui.</summary>
public static class UsersEndpoints
{
    public sealed record RegisterUserRequest(string? Email, string? Name, string? Password);

    public sealed record BecomeSellerResult(UserResponse User, string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/users", RegisterAsync)
            .WithName("RegisterUser")
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/users/me", GetCurrentAsync)
            .WithName("GetCurrentUser")
            .RequireAuthorization()
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/users/me/seller", BecomeSellerAsync)
            .WithName("BecomeSeller")
            .RequireAuthorization()
            .Produces<BecomeSellerResult>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterUserRequest request, IHandler<RegisterUserCommand, UserResponse> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new RegisterUserCommand(request.Email, request.Name, request.Password), ct);

        return result.ToHttpResult(user => Results.Created($"/users/{user.Id}", user));
    }

    private static async Task<IResult> GetCurrentAsync(
        ClaimsPrincipal principal, IHandler<GetUserByIdQuery, UserResponse> handler, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(new GetUserByIdQuery(userId), ct);
        return result.ToHttpResult(user => Results.Ok(user));
    }

    private static async Task<IResult> BecomeSellerAsync(
        ClaimsPrincipal principal, IHandler<BecomeSellerCommand, BecomeSellerResponse> handler, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(new BecomeSellerCommand(userId), ct);
        return result.ToHttpResult(r => Results.Ok(new BecomeSellerResult(r.User, r.AccessToken, "Bearer", r.ExpiresAt)));
    }
}
