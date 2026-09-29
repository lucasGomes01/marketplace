using System.Security.Claims;
using BuildingBlocks.Auth;
using Microsoft.EntityFrameworkCore;
using Users.Api.Infrastructure;
using Users.Domain;

namespace Users.Api.Features.Users;

public static class GetCurrentUser
{
    public sealed record Response(Guid Id, string Email, string Name, UserRole Role);

    public static IEndpointRouteBuilder MapGetCurrentUser(this IEndpointRouteBuilder app)
    {
        app.MapGet("/users/me", Handle)
            .WithName("GetCurrentUser")
            .RequireAuthorization()
            .Produces<Response>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> Handle(ClaimsPrincipal principal, UsersDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var user = userId is null
            ? null
            : await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);

        return user is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found.")
            : Results.Ok(new Response(user.Id, user.Email.Value, user.Name, user.Role));
    }
}
