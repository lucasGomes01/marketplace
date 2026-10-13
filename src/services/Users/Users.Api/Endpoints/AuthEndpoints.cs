using BuildingBlocks.Api;
using BuildingBlocks.Application;
using Users.Application.Login;

namespace Users.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string? Email, string? Password);

    public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", LoginAsync)
            .WithName("Login")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, IHandler<LoginCommand, LoginResponse> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password), ct);

        return result.ToHttpResult(token => Results.Ok(new TokenResponse(token.AccessToken, "Bearer", token.ExpiresAt)));
    }
}
