namespace Catalog.Domain;

public sealed class Product
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 2000;
    public const int CategoryMaxLength = 40;
    public const int BrandMaxLength = 60;
    public const int SkuMaxLength = 40;
    public const int MaxImages = 8;
    public const int ImageUrlMaxLength = 500;
    public const int MaxAttributes = 20;
    public const int AttributeNameMaxLength = 40;
    public const int AttributeValueMaxLength = 120;
    public const int MaxWeightGrams = 1_000_000;
    public const int MaxDimensionCm = 1_000;
    public const int MaxWarrantyMonths = 120;

    public Guid Id { get; private set; }
    public Guid SellerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Money Price { get; private set; } = null!;
    public int Stock { get; private set; }
    public string Category { get; private set; } = ProductCategories.Default;
    public string? Brand { get; private set; }
    public string? Sku { get; private set; }
    public ProductCondition Condition { get; private set; }
    public List<string> ImageUrls { get; private set; } = [];
    public decimal? OriginalPrice { get; private set; }
    public int? WeightGrams { get; private set; }
    public int? LengthCm { get; private set; }
    public int? WidthCm { get; private set; }
    public int? HeightCm { get; private set; }
    public int? WarrantyMonths { get; private set; }
    public bool FreeShipping { get; private set; }
    public List<ProductAttribute> Attributes { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Usado pelo EF Core.
    private Product() { }

    public static Product Create(
        Guid sellerId, string name, string? description, Money price, int stock, TimeProvider clock,
        ProductDetails? details = null)
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
        details ??= new ProductDetails();

        var now = clock.GetUtcNow();
        return new Product
        {
            Id = Guid.CreateVersion7(),
            SellerId = sellerId,
            Name = NormalizeName(name),
            Description = NormalizeDescription(description),
            Price = price,
            Stock = stock,
            Category = ProductCategories.Normalize(details.Category),
            Brand = NormalizeOptional(details.Brand, BrandMaxLength, "brand"),
            Sku = NormalizeOptional(details.Sku, SkuMaxLength, "sku"),
            Condition = Enum.IsDefined(details.Condition)
                ? details.Condition
                : throw new DomainException("Unknown condition.", "condition"),
            ImageUrls = NormalizeImageUrls(details.ImageUrls),
            OriginalPrice = NormalizeOriginalPrice(details.OriginalPrice, price),
            WeightGrams = EnsureRange(details.WeightGrams, 1, MaxWeightGrams, "weightGrams"),
            LengthCm = EnsureRange(details.LengthCm, 1, MaxDimensionCm, "lengthCm"),
            WidthCm = EnsureRange(details.WidthCm, 1, MaxDimensionCm, "widthCm"),
            HeightCm = EnsureRange(details.HeightCm, 1, MaxDimensionCm, "heightCm"),
            WarrantyMonths = EnsureRange(details.WarrantyMonths, 0, MaxWarrantyMonths, "warrantyMonths"),
            FreeShipping = details.FreeShipping,
            Attributes = NormalizeAttributes(details.Attributes),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public bool IsOwnedBy(Guid userId) => SellerId == userId;

    public void ChangePrice(Money price, TimeProvider clock)
    {
        EnsurePositive(price);
        Price = price;
        if (OriginalPrice <= price.Amount)
        {
            OriginalPrice = null; // sem desconto não há preço "de"
        }
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

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{field} must have at most {maxLength} characters.", field);
        }
        return trimmed;
    }

    private static List<string> NormalizeImageUrls(IReadOnlyList<string>? urls)
    {
        var list = (urls ?? []).Select(u => u?.Trim()).Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).ToList();
        if (list.Count > MaxImages)
        {
            throw new DomainException($"At most {MaxImages} images are allowed.", "imageUrls");
        }
        foreach (var url in list)
        {
            // Só a referência da imagem (http/https); o arquivo mora fora do Catalog.
            var valid = url.Length <= ImageUrlMaxLength
                && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
            if (!valid)
            {
                throw new DomainException("Image URLs must be absolute http(s) addresses.", "imageUrls");
            }
        }
        return list;
    }

    private static decimal? NormalizeOriginalPrice(decimal? originalPrice, Money price)
    {
        if (originalPrice is null)
        {
            return null;
        }
        if (decimal.Round(originalPrice.Value, 2) != originalPrice.Value)
        {
            throw new DomainException("Original price must have at most 2 decimal places.", "originalPrice");
        }
        if (originalPrice.Value <= price.Amount)
        {
            throw new DomainException("Original price must be greater than the price.", "originalPrice");
        }
        return originalPrice;
    }

    private static int? EnsureRange(int? value, int min, int max, string field)
    {
        if (value is { } v && (v < min || v > max))
        {
            throw new DomainException($"{field} must be between {min} and {max}.", field);
        }
        return value;
    }

    private static List<ProductAttribute> NormalizeAttributes(IReadOnlyList<ProductAttribute>? attributes)
    {
        var list = new List<ProductAttribute>();
        foreach (var attribute in attributes ?? [])
        {
            var name = attribute.Name?.Trim() ?? string.Empty;
            var value = attribute.Value?.Trim() ?? string.Empty;
            if (name.Length == 0 && value.Length == 0)
            {
                continue; // linha em branco do formulário
            }
            if (name.Length == 0 || value.Length == 0
                || name.Length > AttributeNameMaxLength || value.Length > AttributeValueMaxLength)
            {
                throw new DomainException(
                    $"Each attribute needs a name (up to {AttributeNameMaxLength} characters) and a value (up to {AttributeValueMaxLength}).",
                    "attributes");
            }
            list.Add(new ProductAttribute(name, value));
        }
        if (list.Count > MaxAttributes)
        {
            throw new DomainException($"At most {MaxAttributes} attributes are allowed.", "attributes");
        }
        return list;
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
