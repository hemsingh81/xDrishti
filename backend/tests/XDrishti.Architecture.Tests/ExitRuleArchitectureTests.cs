using System.Reflection;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Criterion 13 of P4-01, as P4-02 and P4-03 leave it: the only rules a shipped assembly implements are the three trails of P4-03 (<c>ATR_TRAIL</c>, <c>STRUCTURE_TRAIL</c>, <c>EMA_TRAIL</c>), which leave in one fill and never ask for a share, and the only other production policies are the data of <c>LegPolicy</c> that <c>Simulator.Create</c> builds from the accepted exits (P4-02). The rules that make legs are the test-only ones (<c>TestLadderRule</c>, <c>ScriptedRule</c>) in the test project: none is named
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

    private static readonly string[] TrailRules = ["XDrishti.Simulation.Walking.AtrTrailRule", "XDrishti.Simulation.Walking.StructureTrailRule", "XDrishti.Simulation.Walking.EmaTrailRule"];

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
        Shipped.SelectMany(a => Implementers(a, contract)).Order().ShouldBe(TrailRules.Order(), "the shipped rules are the three trails, which make no leg; any other would make legs in a production run");
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
    public void The_walker_takes_a_rule_or_a_policy_only_through_an_internal_overload()
    {
        // the production path to a rule or a policy is Simulator.Create (a trail rule of P4-03, or a LegPolicy of P4-02 for PARTIAL_BE, PARTIAL_TRAIL or the time stop); the refusal of every other set is held by SimulatorCreateTests and ExitBoundaryTests, and the walker takes an exit context only through an internal overload
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
