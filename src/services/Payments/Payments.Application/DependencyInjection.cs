using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Payments.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsApplication(this IServiceCollection services) =>
        services.AddHandlersFrom(typeof(DependencyInjection).Assembly);
}
