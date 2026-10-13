using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Users.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddUsersApplication(this IServiceCollection services) =>
        services.AddHandlersFrom(typeof(DependencyInjection).Assembly);
}
