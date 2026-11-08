namespace Payments.Infrastructure.Mastercard;

/// <summary>
/// Credenciais do projeto criado em developer.mastercard.com (seção <c>Mastercard</c>). Só são usadas com
/// <c>Payments:Provider=Mastercard</c>. A chave de assinatura (.p12 ou .pem) fica fora do repositório.
/// </summary>
public sealed class MastercardOptions
{
    public const string SectionName = "Mastercard";

    /// <summary>Sandbox: <c>https://sandbox.api.mastercard.com</c>.</summary>
    public string BaseUrl { get; set; } = "https://sandbox.api.mastercard.com";

    public string ConsumerKey { get; set; } = string.Empty;

    /// <summary>Caminho do arquivo .p12 (ou .pem com a chave privada RSA) baixado do portal.</summary>
    public string SigningKeyPath { get; set; } = string.Empty;

    public string SigningKeyPassword { get; set; } = string.Empty;

    /// <summary>Partner ID do Mastercard Send (vem no projeto do portal).</summary>
    public string PartnerId { get; set; } = string.Empty;

    /// <summary>País de origem do pagamento (ISO 3166 alpha-3).</summary>
    public string OriginationCountry { get; set; } = "BRA";

    public int TimeoutSeconds { get; set; } = 15;
}
