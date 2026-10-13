using System.Reflection;
using Catalog.Application.CreateProduct;
using Catalog.Domain;
using Catalog.Infrastructure.Persistence;

namespace Catalog.UnitTests;

/// <summary>Regra de dependência do ADR-004 (as setas só apontam para dentro).</summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Product).Assembly;
    private static readonly Assembly Application = typeof(CreateProductHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(CatalogDbContext).Assembly;

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
            "Catalog.Application", "Catalog.Infrastructure", "Catalog.Api", "BuildingBlocks",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions");
    }

    [Fact]
    public void Application_does_not_know_infrastructure_or_web()
    {
        AssertDoesNotReference(Application,
            "Catalog.Infrastructure", "Catalog.Api", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
            "BuildingBlocks.Auth", "BuildingBlocks.Api", "Confluent.Kafka");
    }

    [Fact]
    public void Infrastructure_does_not_know_the_api()
    {
        AssertDoesNotReference(Infrastructure, "Catalog.Api", "BuildingBlocks.Api");
    }
}
