namespace Catalog.Domain;

public enum ProductCondition
{
    New,
    Used,
    Refurbished,
}

/// <summary>Categorias da vitrine. O código é estável (vai na URL e no banco); o nome em português fica no front.</summary>
public static class ProductCategories
{
    public const string Default = "outros";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "eletronicos", "informatica", "celulares", "casa", "eletrodomesticos", "moda", "beleza",
        "esportes", "ferramentas", "brinquedos", "livros", "automotivo", "pet", Default,
    };

    /// <summary>Vazio vira <see cref="Default"/>; desconhecida é erro de domínio.</summary>
    public static string Normalize(string? category)
    {
        var code = category?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
        {
            return Default;
        }
        return All.Contains(code) ? code : throw new DomainException("Unknown category.", "category");
    }
}

/// <summary>Uma linha da ficha técnica (ex.: "Voltagem" = "220V").</summary>
public sealed record ProductAttribute(string Name, string Value);

/// <summary>
/// Dados do anúncio além do essencial (nome, preço, estoque). Tudo opcional: um produto sem detalhes
/// cai na categoria "outros", novo, sem fotos.
/// </summary>
public sealed record ProductDetails
{
    public string? Category { get; init; }
    public string? Brand { get; init; }
    public string? Sku { get; init; }
    public ProductCondition Condition { get; init; } = ProductCondition.New;
    public IReadOnlyList<string>? ImageUrls { get; init; }

    /// <summary>Preço "de" (antes do desconto), na mesma moeda do preço.</summary>
    public decimal? OriginalPrice { get; init; }

    public int? WeightGrams { get; init; }
    public int? LengthCm { get; init; }
    public int? WidthCm { get; init; }
    public int? HeightCm { get; init; }
    public int? WarrantyMonths { get; init; }
    public bool FreeShipping { get; init; }
    public IReadOnlyList<ProductAttribute>? Attributes { get; init; }
}
