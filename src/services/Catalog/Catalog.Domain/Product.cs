namespace Catalog.Domain;

public sealed class Product
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 2000;

    public Guid Id { get; private set; }
    public Guid SellerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Money Price { get; private set; } = null!;
    public int Stock { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Usado pelo EF Core.
    private Product() { }

    public static Product Create(
        Guid sellerId, string name, string? description, Money price, int stock, TimeProvider clock)
    {
        if (sellerId == Guid.Empty)
        {
            throw new DomainException("Seller is required.", "sellerId");
        }
        if (stock < 0)
        {
            throw new DomainException("Stock cannot be negative.", "stock");
        }
        EnsurePositive(price);

        var now = clock.GetUtcNow();
        return new Product
        {
            Id = Guid.CreateVersion7(),
            SellerId = sellerId,
            Name = NormalizeName(name),
            Description = NormalizeDescription(description),
            Price = price,
            Stock = stock,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public bool IsOwnedBy(Guid userId) => SellerId == userId;

    public void ChangePrice(Money price, TimeProvider clock)
    {
        EnsurePositive(price);
        Price = price;
        UpdatedAt = clock.GetUtcNow();
    }

    public void AddStock(int quantity, TimeProvider clock)
    {
        EnsurePositiveQuantity(quantity);
        Stock += quantity;
        UpdatedAt = clock.GetUtcNow();
    }

    public void RemoveStock(int quantity, TimeProvider clock)
    {
        EnsurePositiveQuantity(quantity);
        if (quantity > Stock)
        {
            throw new DomainException($"Only {Stock} unit(s) in stock; cannot remove {quantity}.", "stock");
        }
        Stock -= quantity;
        UpdatedAt = clock.GetUtcNow();
    }

    private static string NormalizeName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainException("Name is required.", "name");
        }
        if (trimmed.Length > NameMaxLength)
        {
            throw new DomainException($"Name must have at most {NameMaxLength} characters.", "name");
        }
        return trimmed;
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        if (trimmed.Length > DescriptionMaxLength)
        {
            throw new DomainException($"Description must have at most {DescriptionMaxLength} characters.", "description");
        }
        return trimmed;
    }

    private static void EnsurePositive(Money price)
    {
        if (price.Amount <= 0)
        {
            throw new DomainException("Price must be greater than zero.", "price");
        }
    }

    private static void EnsurePositiveQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.", "quantity");
        }
    }
}
