using System.Reflection;
using Cart.Application.AddItem;
using Cart.Domain;
using Cart.Infrastructure.Persistence;

namespace Cart.UnitTests;

/// <summary>Regra de dependência do ADR-004 (as setas só apontam para dentro).</summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(ShoppingCart).Assembly;
    private static readonly Assembly Application = typeof(AddItemHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(CartDbContext).Assembly;

    private static void AssertDoesNotReference(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var violations = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(violations.Count == 0,
            $"{assembly.GetName().Name} não pode referenciar: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Domain_depends_on_nothing_but_the_base_library()
    {
        AssertDoesNotReference(Domain,
            "Cart.Application", "Cart.Infrastructure", "Cart.Api", "BuildingBlocks",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions");
    }

    [Fact]
    public void Application_does_not_know_infrastructure_web_or_http()
    {
        AssertDoesNotReference(Application,
            "Cart.Infrastructure", "Cart.Api", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
            "Microsoft.Extensions.Http", "BuildingBlocks.Auth", "BuildingBlocks.Api", "Confluent.Kafka");
    }

    [Fact]
    public void Infrastructure_does_not_know_the_api()
    {
        AssertDoesNotReference(Infrastructure, "Cart.Api", "BuildingBlocks.Api");
    }
}
