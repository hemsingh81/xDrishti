using System.Reflection;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// P3-03d criterion 25: the swing walk and the position gate keep the library pure and keep its stages apart (no rule of <see cref="SimulationArchitectureTests"/> is relaxed): the two new public surfaces are <c>SwingHold</c> and the
/// fields of <c>WalkRules</c>, everything else new is internal; the per-minute rows live in one internal routine that the intraday loop and the swing loop both depend on; the gate is a detail of <c>Running</c>.
/// </summary>
public sealed class SwingArchitectureTests
{
    private static readonly Assembly Simulation = typeof(XDrishti.Simulation.SimulationEngine).Assembly;

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    private static Type Internal(string fullName)
    {
        var type = Simulation.GetType(fullName);
        type.ShouldNotBeNull(fullName);
        type.IsPublic.ShouldBeFalse(fullName + " is internal");
        return type;
    }

    [Fact]
    public void The_public_surface_of_the_walking_stage_is_the_walker_its_rules_and_outcome_and_the_hold()
    {
        Simulation.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Simulation.Walking").Select(t => t.Name).Order().ShouldBe(["SwingHold", "TradeWalker", "WalkOutcome", "WalkRules"]);
    }

    [Theory]
    [InlineData("XDrishti.Simulation.Walking.MinuteRule")]
    [InlineData("XDrishti.Simulation.Walking.SwingWalk")]
    [InlineData("XDrishti.Simulation.Walking.WalkResults")]
    [InlineData("XDrishti.Simulation.Walking.Position")]
    [InlineData("XDrishti.Simulation.Walking.Slip")]
    [InlineData("XDrishti.Simulation.Walking.Minute")]
    [InlineData("XDrishti.Simulation.Walking.ExitFlags")]
    [InlineData("XDrishti.Simulation.Walking.MinuteVerdict")]
    [InlineData("XDrishti.Simulation.Running.PositionGate")]
    public void The_new_machinery_is_internal(string fullName)
    {
        Internal(fullName);
        Simulation.GetExportedTypes().ShouldNotContain(t => t.FullName == fullName);
    }

    [Fact]
    public void The_hold_is_a_sealed_record_with_a_private_constructor_and_depends_on_the_domain_only()
    {
        var hold = Simulation.GetType("XDrishti.Simulation.Walking.SwingHold")!;

        hold.IsSealed.ShouldBeTrue();
        hold.GetConstructors().ShouldBeEmpty("created by Create, which validates");
        hold.GetMethod("Create", BindingFlags.Public | BindingFlags.Static).ShouldNotBeNull();
        ShouldHaveNone(Types.InAssembly(Simulation).That().HaveName("SwingHold").ShouldNot().HaveDependencyOnAny("XDrishti.Indicators", "XDrishti.Strategies", "XDrishti.Simulation.Scanning", "XDrishti.Simulation.Running", "XDrishti.Simulation.Replay").GetResult());
    }

    [Fact]
    public void The_intraday_loop_and_the_swing_loop_both_depend_on_the_one_routine_of_the_exit_rows_and_nothing_else_does()
    {
        Types.InAssembly(Simulation).That().HaveName("TradeWalker").Should().HaveDependencyOn("XDrishti.Simulation.Walking.MinuteRule").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Simulation).That().HaveName("SwingWalk").Should().HaveDependencyOn("XDrishti.Simulation.Walking.MinuteRule").GetResult().IsSuccessful.ShouldBeTrue();
        ShouldHaveNone(Types.InAssembly(Simulation).That().DoNotHaveName("TradeWalker").And().DoNotHaveName("SwingWalk").And().DoNotHaveName("MinuteRule").ShouldNot().HaveDependencyOn("XDrishti.Simulation.Walking.MinuteRule").GetResult());
    }

    [Fact]
    public void The_gate_is_used_by_the_simulator_alone_and_the_replay_and_the_root_never_see_it()
    {
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Replay").Or().ResideInNamespace("XDrishti.Simulation.Walking").Or().ResideInNamespace("XDrishti.Simulation.Scanning").ShouldNot().HaveDependencyOn("XDrishti.Simulation.Running.PositionGate").GetResult());
        Types.InAssembly(Simulation).That().HaveName("Simulator").Should().HaveDependencyOn("XDrishti.Simulation.Running.PositionGate").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_walking_stage_reads_the_calendar_it_is_given_and_fetches_none()
    {
        // The calendar is a value of the Domain passed in with the rules; no port, no service, no clock.
        ShouldHaveNone(Types.InAssembly(Simulation).That().ResideInNamespace("XDrishti.Simulation.Walking").ShouldNot().HaveDependencyOnAny("XDrishti.Application", "XDrishti.Infrastructure", "System.TimeProvider", "System.Random", "System.Threading.Tasks", "System.IO").GetResult());
    }
}
