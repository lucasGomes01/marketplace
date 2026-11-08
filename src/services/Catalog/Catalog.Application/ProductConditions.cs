using Catalog.Domain;

namespace Catalog.Application;

/// <summary>Condição como texto na API ("New", "Used", "Refurbished", sem diferenciar maiúsculas).</summary>
public static class ProductConditions
{
    /// <summary>Vazio é "New". Número ("1") não vale: só o nome.</summary>
    public static bool TryParse(string? text, out ProductCondition condition)
    {
        condition = ProductCondition.New;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }
        return text.Trim().ToLowerInvariant() switch
        {
            "new" => Set(ProductCondition.New, out condition),
            "used" => Set(ProductCondition.Used, out condition),
            "refurbished" => Set(ProductCondition.Refurbished, out condition),
            _ => false,
        };
    }

    private static bool Set(ProductCondition value, out ProductCondition condition)
    {
        condition = value;
        return true;
    }
}
