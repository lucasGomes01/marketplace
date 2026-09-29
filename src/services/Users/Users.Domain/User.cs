namespace Users.Domain;

public sealed class User
{
    public const int NameMaxLength = 100;

    public Guid Id { get; private set; }
    public Email Email { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    // Usado pelo EF Core.
    private User() { }

    public static User Register(string email, string name, UserRole role, string passwordHash, TimeProvider clock)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length == 0)
        {
            throw new DomainException("Name is required.", "name");
        }
        if (trimmedName.Length > NameMaxLength)
        {
            throw new DomainException($"Name must have at most {NameMaxLength} characters.", "name");
        }
        if (!Enum.IsDefined(role))
        {
            throw new DomainException($"Role '{role}' is not valid.", "role");
        }
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash is required.", "passwordHash");
        }

        return new User
        {
            Id = Guid.CreateVersion7(),
            Email = Email.Create(email),
            Name = trimmedName,
            Role = role,
            PasswordHash = passwordHash,
            CreatedAt = clock.GetUtcNow(),
        };
    }
}
