using System.Security.Claims;

namespace BuildingBlocks.Auth;

/// <summary>Nomes de claims usados no token (sem o mapeamento legado para URIs do .NET).</summary>
public static class MarketplaceClaims
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Role = "role";

    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserId), out var id) ? id : null;
}

public static class MarketplacePolicies
{
    public const string Buyer = "Buyer";
    public const string Seller = "Seller";
}
