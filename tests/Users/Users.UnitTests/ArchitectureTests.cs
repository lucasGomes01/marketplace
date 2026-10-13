using System.Reflection;
using Users.Application.Register;
using Users.Domain;
using Users.Infrastructure.Persistence;

namespace Users.UnitTests;

/// <summary>
/// Regra de dependência do ADR-004: as setas só apontam para dentro (Api → Infrastructure → Application → Domain).
/// Olha as referências reais de cada assembly compilado.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(User).Assembly;
    private static readonly Assembly Application = typeof(RegisterUserHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(UsersDbContext).Assembly;

    private static IEnumerable<string> ReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    private static void AssertDoesNotReference(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var violations = ReferencesOf(assembly)
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(violations.Count == 0,
            $"{assembly.GetName().Name} não pode referenciar: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Domain_depends_on_nothing_but_the_base_library()
    {
        AssertDoesNotReference(Domain,
            "Users.Application", "Users.Infrastructure", "Users.Api", "BuildingBlocks",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions");
    }

    [Fact]
    public void Application_does_not_know_infrastructure_or_web()
    {
        AssertDoesNotReference(Application,
            "Users.Infrastructure", "Users.Api", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
            "BuildingBlocks.Auth", "BuildingBlocks.Api", "Confluent.Kafka");
    }

    [Fact]
    public void Infrastructure_does_not_know_the_api()
    {
        AssertDoesNotReference(Infrastructure, "Users.Api", "BuildingBlocks.Api");
    }

    [Fact]
    public void Application_handlers_are_the_use_cases_exposed_to_the_api()
    {
        var handlers = Application.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(t => t.Name)
            .Order();

        Assert.Equal(["GetUserByIdHandler", "LoginHandler", "RegisterUserHandler"], handlers);
    }
}
