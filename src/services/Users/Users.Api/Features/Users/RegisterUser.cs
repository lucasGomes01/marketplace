using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Users.Api.Infrastructure;
using Users.Domain;

namespace Users.Api.Features.Users;

public static class RegisterUser
{
    public const int PasswordMinLength = 8;

    public sealed record Request(string? Email, string? Name, string? Password, string? Role);

    public sealed record Response(Guid Id, string Email, string Name, UserRole Role);

    public static IEndpointRouteBuilder MapRegisterUser(this IEndpointRouteBuilder app)
    {
        app.MapPost("/users", Handle)
            .WithName("RegisterUser")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> Handle(
        Request request, UsersDbContext db, IPasswordHasher hasher, TimeProvider clock, CancellationToken ct)
    {
        // Regras que não pertencem à entidade (a senha em texto puro nunca chega ao domínio).
        var errors = new Dictionary<string, string[]>();
        if ((request.Password?.Length ?? 0) < PasswordMinLength)
        {
            errors["password"] = [$"Password must have at least {PasswordMinLength} characters."];
        }
        if (!Enum.GetNames<UserRole>().Contains(request.Role, StringComparer.OrdinalIgnoreCase))
        {
            errors["role"] = [$"Role must be one of: {string.Join(", ", Enum.GetNames<UserRole>())}."];
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        User user;
        try
        {
            var role = Enum.Parse<UserRole>(request.Role!, ignoreCase: true);
            user = User.Register(request.Email!, request.Name!, role, hasher.Hash(request.Password!), clock);
        }
        catch (DomainException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.Field ?? "request"] = [ex.Message] });
        }

        if (await db.Users.AnyAsync(u => u.Email == user.Email, ct))
        {
            return EmailAlreadyRegistered();
        }

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Corrida entre duas requisições com o mesmo email: o índice único é a garantia final.
            return EmailAlreadyRegistered();
        }

        return Results.Created($"/users/{user.Id}", new Response(user.Id, user.Email.Value, user.Name, user.Role));
    }

    private static IResult EmailAlreadyRegistered() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered.");
}
