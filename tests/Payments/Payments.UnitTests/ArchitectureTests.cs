using System.Reflection;
using Payments.Application.RegisterPayoutCard;
using Payments.Domain;
using Payments.Infrastructure.Persistence;

namespace Payments.UnitTests;

/// <summary>Regra de dependência do ADR-004 (as setas só apontam para dentro).</summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Payout).Assembly;
    private static readonly Assembly Application = typeof(RegisterPayoutCardHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(PaymentsDbContext).Assembly;

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
            "Payments.Application", "Payments.Infrastructure", "Payments.Api", "BuildingBlocks",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions");
    }

    [Fact]
    public void Application_does_not_know_infrastructure_or_web()
    {
        AssertDoesNotReference(Application,
            "Payments.Infrastructure", "Payments.Api", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
            "BuildingBlocks.Auth", "BuildingBlocks.Api", "Confluent.Kafka", "System.Net.Http");
    }

    [Fact]
    public void Infrastructure_does_not_know_the_api()
    {
        AssertDoesNotReference(Infrastructure, "Payments.Api", "BuildingBlocks.Api");
    }
}
