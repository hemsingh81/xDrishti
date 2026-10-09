using System.Reflection;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Enforces the dependency rule: Domain ← Application ← Infrastructure ← hosts.
/// A failing test here means a layer reached "outwards" — fix the design, do not relax the rule.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_else() =>
        AssertNoDependency(Domain,
            "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker",
            "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "Microsoft.Extensions", "Serilog");

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_frameworks() =>
        AssertNoDependency(Application,
            "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker",
            "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "Serilog");

    [Fact]
    public void Infrastructure_does_not_depend_on_hosts() =>
        AssertNoDependency(Infrastructure, "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "Microsoft.AspNetCore");

    [Fact]
    public void Use_case_handlers_are_sealed_and_not_public()
    {
        var handlers = Types.InAssembly(Application).That().HaveNameEndingWith("Handler").GetTypes().ToList();

        handlers.ShouldNotBeEmpty();
        handlers.ShouldAllBe(t => t.IsSealed && !t.IsPublic);
    }

    [Fact]
    public void Domain_entities_are_sealed_or_abstract()
    {
        var result = Types.InAssembly(Domain).That().AreClasses().And().AreNotNested()
            .Should().BeSealed().Or().BeAbstract().Or().HaveName("Result")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    private static string Failing(ArchResult result) =>
        "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
