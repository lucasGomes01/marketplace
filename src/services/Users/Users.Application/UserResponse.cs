using Users.Domain;

namespace Users.Application;

/// <summary>Dados públicos de um usuário (nunca inclui o hash da senha).</summary>
public sealed record UserResponse(Guid Id, string Email, string Name, UserRole Role)
{
    public static UserResponse From(User user) => new(user.Id, user.Email.Value, user.Name, user.Role);
}
