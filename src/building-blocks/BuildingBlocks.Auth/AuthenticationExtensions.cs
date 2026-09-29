using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Auth;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Autenticação JWT padrão do marketplace. Em cada serviço:
    /// <c>builder.Services.AddMarketplaceJwtAuth(builder.Configuration);</c> +
    /// <c>app.UseAuthentication(); app.UseAuthorization();</c>
    /// </summary>
    public static IServiceCollection AddMarketplaceJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = JwtValidation.CreateParameters(jwt.Value);
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(MarketplacePolicies.Buyer, policy => policy.RequireRole(MarketplacePolicies.Buyer))
            .AddPolicy(MarketplacePolicies.Seller, policy => policy.RequireRole(MarketplacePolicies.Seller));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JwtTokenIssuer>();

        return services;
    }
}
