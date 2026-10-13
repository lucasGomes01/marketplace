using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCartApplication(this IServiceCollection services) =>
        services.AddHandlersFrom(typeof(DependencyInjection).Assembly);
}
