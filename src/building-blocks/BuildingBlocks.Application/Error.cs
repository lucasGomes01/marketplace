namespace BuildingBlocks.Application;

public enum ErrorType
{
    Validation,
    Unauthorized,
    NotFound,
    Conflict,

    /// <summary>Uma dependência (outro serviço) está fora do ar; tentar de novo pode funcionar.</summary>
    Unavailable,
}

/// <summary>
/// Falha esperada de um caso de uso. <see cref="Code"/> é estável (ex.: "password.too_short") e serve para o
/// front traduzir; <see cref="Message"/> é texto para humanos; <see cref="Field"/> liga o erro a um campo.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type, string? Field = null)
{
    public static Error Validation(string? field, string code, string message) =>
        new(code, message, ErrorType.Validation, field);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}
