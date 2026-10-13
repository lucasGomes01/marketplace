namespace Users.Domain;

/// <summary>
/// Uma conta do marketplace. Toda conta pode comprar; vender é um perfil que a própria pessoa ativa.
/// </summary>
public sealed class User
{
    public const int NameMaxLength = 100;

    public Guid Id { get; private set; }
    public Email Email { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsSeller { get; private set; }
    public DateTimeOffset? SellerSince { get; private set; }

    /// <summary>Papéis ativos: sempre <see cref="UserRole.Buyer"/>; <see cref="UserRole.Seller"/> após ativar.</summary>
    public IReadOnlyList<UserRole> Roles => IsSeller ? [UserRole.Buyer, UserRole.Seller] : [UserRole.Buyer];

    // Usado pelo EF Core.
    private User() { }

    public static User Register(string email, string name, string passwordHash, TimeProvider clock)
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
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash is required.", "passwordHash");
        }

        return new User
        {
            Id = Guid.CreateVersion7(),
            Email = Email.Create(email),
            Name = trimmedName,
            PasswordHash = passwordHash,
            CreatedAt = clock.GetUtcNow(),
        };
    }

    /// <summary>Ativa o perfil de vendedor. Idempotente: ativar de novo mantém a data original.</summary>
    public void BecomeSeller(TimeProvider clock)
    {
        if (IsSeller)
        {
            return;
        }

        IsSeller = true;
        SellerSince = clock.GetUtcNow();
    }
}
