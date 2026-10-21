using System.Reflection;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Criterion 13 of P4-01: no production path makes a leg. The only exit rules are the test-only ones (<c>TestLadderRule</c>, <c>ScriptedRule</c>) in the test project: no type of any shipped assembly implements <c>IExitRule</c>, none is named
/// like a test rule, and no shipped assembly references the test project. The check has a sentinel, an implementer in this test project that the same code must find, so that a rule that finds nothing cannot pass for the wrong reason.
/// </summary>
public sealed class ExitRuleArchitectureTests
{
    private static readonly Assembly Simulation = typeof(XDrishti.Simulation.SimulationEngine).Assembly;

    private static readonly Assembly[] Shipped =
    [
        Simulation,
        typeof(XDrishti.Domain.Common.Result).Assembly,
        typeof(XDrishti.Indicators.FeatureFrame).Assembly,
        typeof(XDrishti.Strategies.Problem).Assembly,
        typeof(XDrishti.SimExport.ExportSpellings).Assembly,
        typeof(XDrishti.SimReader.ChartReader).Assembly,
        typeof(XDrishti.Application.DependencyInjection).Assembly,
        typeof(XDrishti.Infrastructure.DependencyInjection).Assembly,
        Assembly.Load("XDrishti.Api"),
    ];

    private static readonly string[] TestRuleNames = ["TestLadderRule", "ScriptedRule"];

    public interface IMarker;

    private sealed class SentinelImplementer : IMarker;

    private static IEnumerable<string> Implementers(Assembly assembly, Type contract) =>
        assembly.GetTypes().Where(t => t != contract && !t.IsInterface && contract.IsAssignableFrom(t)).Select(t => t.FullName ?? t.Name);

    [Fact]
    public void The_contract_of_a_rule_is_internal_to_the_simulation_library_and_no_shipped_assembly_implements_it()
    {
        var contract = Simulation.GetType("XDrishti.Simulation.Walking.IExitRule");

        contract.ShouldNotBeNull();
        contract.IsInterface.ShouldBeTrue();
        contract.IsPublic.ShouldBeFalse("a rule is internal to XDrishti.Simulation; only the test project (a friend assembly) writes one");
        Shipped.SelectMany(a => Implementers(a, contract)).ShouldBeEmpty("a production exit rule would make legs in a production run");
    }

    [Fact]
    public void No_shipped_assembly_holds_a_test_rule_or_references_the_test_project()
    {
        foreach (var assembly in Shipped)
        {
            assembly.GetTypes().Select(t => t.Name).Intersect(TestRuleNames).ShouldBeEmpty(assembly.GetName().Name);
            assembly.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.Simulation.Tests", assembly.GetName().Name);
        }
    }

    [Fact]
    public void The_simulator_still_refuses_every_exit_set_but_one_fixed_rr()
    {
        // the production path to a rule would be Simulator.Create; the refusal is held by SimulatorCreateTests, and the walker takes a rule only through an internal overload
        var overloads = typeof(XDrishti.Simulation.Walking.TradeWalker).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Walk").ToList();

        overloads.Count.ShouldBe(1, "the overload with an exit context is internal");
        overloads[0].GetParameters().Length.ShouldBe(3);
    }

    [Fact]
    public void The_sentinel_an_implementer_of_a_contract_is_found_by_the_same_check()
    {
        Implementers(typeof(ExitRuleArchitectureTests).Assembly, typeof(IMarker)).ShouldContain(name => name.EndsWith("SentinelImplementer", StringComparison.Ordinal));
    }
}
