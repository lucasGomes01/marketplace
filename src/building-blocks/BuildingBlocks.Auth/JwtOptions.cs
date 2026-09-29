using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.Auth;

/// <summary>Configuração da seção <c>Jwt</c>, igual em todos os serviços.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Chave HS256: no mínimo 32 caracteres (256 bits). Nunca versionar a chave de produção.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int ExpirationMinutes { get; set; } = 60;
}
