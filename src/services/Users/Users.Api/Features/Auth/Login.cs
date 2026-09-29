using BuildingBlocks.Auth;
using Microsoft.EntityFrameworkCore;
using Users.Api.Infrastructure;
using Users.Domain;

namespace Users.Api.Features.Auth;

public static class Login
{
    public sealed record Request(string? Email, string? Password);

    public sealed record Response(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

    // Hash de uma senha aleatória: quando o email não existe, verificamos contra ele para gastar o mesmo tempo
    // de um login real e não revelar, pelo tempo de resposta, quais emails estão cadastrados.
    private static string? _dummyPasswordHash;

    public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", Handle)
            .WithName("Login")
            .Produces<Response>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> Handle(
        Request request, UsersDbContext db, IPasswordHasher hasher, JwtTokenIssuer tokens, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["Email is required."];
        }
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors["password"] = ["Password is required."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var user = await FindByEmail(db, request.Email!, ct);
        _dummyPasswordHash ??= hasher.Hash(Guid.NewGuid().ToString());

        var passwordMatches = hasher.Verify(user?.PasswordHash ?? _dummyPasswordHash, request.Password!);
        if (user is null || !passwordMatches)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");
        }

        var token = tokens.Issue(user.Id, user.Email.Value, user.Role.ToString());
        return Results.Ok(new Response(token.Value, "Bearer", token.ExpiresAt));
    }

    private static async Task<User?> FindByEmail(UsersDbContext db, string rawEmail, CancellationToken ct)
    {
        Email email;
        try
        {
            email = Email.Create(rawEmail);
        }
        catch (DomainException)
        {
            // Formato inválido é tratado como credencial inválida, sem dar pistas.
            return null;
        }

        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);
    }
}
