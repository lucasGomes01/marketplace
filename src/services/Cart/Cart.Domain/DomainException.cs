namespace Cart.Domain;

/// <summary>Violação de uma regra de negócio do domínio.</summary>
/// <param name="field">Atributo que violou a regra (ex.: "quantity"), quando aplicável.</param>
public class DomainException(string message, string? field = null) : Exception(message)
{
    public string? Field { get; } = field;
}
