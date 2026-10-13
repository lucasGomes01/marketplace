using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Application;

/// <summary>Um caso de uso. Endpoints dependem desta abstração, não da classe concreta.</summary>
public interface IHandler<in TRequest, TResponse>
{
    Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

public static class HandlerRegistration
{
    /// <summary>
    /// Registra todo handler concreto do assembly. Caso de uso novo = classe nova; nada mais muda (aberto/fechado).
    /// </summary>
    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        var registrations =
            from type in assembly.GetTypes()
            where type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
            from contract in type.GetInterfaces()
            where contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IHandler<,>)
            select (contract, type);

        foreach (var (contract, implementation) in registrations)
        {
            services.AddScoped(contract, implementation);
        }

        return services;
    }
}
