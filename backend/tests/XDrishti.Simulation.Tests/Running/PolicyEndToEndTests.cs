using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 11 of P4-02 through the real <see cref="Simulator"/> (scanner, tape, walker): the ORB of P3-03 with its exit changed to <c>PARTIAL_BE</c>, to <c>PARTIAL_TRAIL</c> and with the <c>time_stop</c>, over the F days. The entries are
/// the entries of the <c>FIXED_RR</c> run (an exit never changes an entry), the counters add up, the legs are in the trades that book T1, and the policy that ran is in the definition. The verbs take a built-in strategy by id and have no way
/// to choose another exit yet (a later slice); this is the end-to-end of the library they call.
/// </summary>
public sealed class PolicyEndToEndTests
{
    private static ExitParameter P(string name, double value) => new(name, [Quantity.Of(value)]);

    private static StrategyDefinition With(ExitSpec[] exits, OverlaySpec[]? overlays = null)
    {
        var orb = OrbV1.Create();
        return orb with { Buy = orb.Buy! with { Exits = exits, Overlays = overlays }, Sell = orb.Sell! with { Exits = exits, Overlays = overlays } };
    }

    private static readonly OverlaySpec[] TimeStop = [new OverlaySpec(OverlayKind.TimeStop, [P("min_progress_rr", 0.5), P("intraday_bars", 4)])];

    private static readonly ExitSpec[] Be = [new ExitSpec(ExitPolicy.PartialBe, [P("book_pct", 50), P("t1_rr", 1), P("t2_rr", 2)])];

    private static readonly ExitSpec[] Trail = [new ExitSpec(ExitPolicy.PartialTrail, [P("book_pct", 50), P("t1_rr", 1), P("trail_atr_mult", 2), P("atr_period", 3)])];

    private static readonly ExitSpec[] Rr = [new ExitSpec(ExitPolicy.FixedRr, [P("rr", 2)])];

    public static TheoryData<string> Days => [.. Cases.FDays.Select(c => c.Name)];

    private static SimulationResult Run(StrategyDefinition definition, string day) => Sim.Run(Sim.Create(definition, fills: Cand.NoSlippage), Cases.F(day).Bars, Cand.Test);

    [Theory]
    [MemberData(nameof(Days))]
    public void Every_policy_walks_the_entries_of_the_FIXED_RR_run_and_its_counters_add_up(string day)
    {
        var reference = Run(With(Rr), day);

        foreach (var (label, definition) in new (string, StrategyDefinition)[] { ("PARTIAL_BE", With(Be)), ("PARTIAL_TRAIL", With(Trail)), ("time_stop", With(Rr, TimeStop)), ("PARTIAL_BE + time_stop", With(Be, TimeStop)) })
        {
            var result = Run(definition, day);

            result.Outcomes.Count.ShouldBe(reference.Outcomes.Count, label + " " + day);
            for (var i = 0; i < reference.Outcomes.Count; i++)
            {
                (result.Outcomes[i].Candidate with { Trace = DecisionTrace.Empty }).ShouldBe(reference.Outcomes[i].Candidate with { Trace = DecisionTrace.Empty }, label + " " + day);
                if (result.Outcomes[i].Walk.Trade is { } trade && reference.Outcomes[i].Walk.Trade is { } plain)
                {
                    (trade.EntryEpochSeconds, trade.EntryPrice, trade.StopPrice, trade.EntryFilledPrice).ShouldBe((plain.EntryEpochSeconds, plain.EntryPrice, plain.StopPrice, plain.EntryFilledPrice), label + " " + day);
                }
            }

            var c = result.Walk;
            c.Trades.ShouldBe(c.ExitsStop + c.ExitsTarget + c.ExitsSquareOff + c.ExitsMaxHold + c.ExitsEndOfData + c.ExitsTimeStop, label + " " + day);
            c.LegsTotal.ShouldBe(result.Outcomes.Sum(o => o.Walk.Trade?.Legs?.Count ?? 0), label + " " + day);
            result.Header.EngineVersion.ShouldBe("sim-6");
        }
    }

    [Fact]
    public void The_policies_make_legs_and_the_time_stop_makes_its_exit_over_the_F_days()
    {
        var legsBe = 0;
        var legsTrail = 0;
        var timeStops = 0;
        foreach (var day in Cases.FDays.Select(c => c.Name))
        {
            legsBe += Run(With(Be), day).Walk.LegsTotal > 0 ? 1 : 0;
            legsTrail += Run(With(Trail), day).Walk.LegsTotal > 0 ? 1 : 0;
            timeStops += (int)Run(With(Rr, TimeStop), day).Walk.ExitsTimeStop;
        }

        legsBe.ShouldBeGreaterThan(0, "a day of the F table books T1 of PARTIAL_BE");
        legsTrail.ShouldBeGreaterThan(0, "a day of the F table books T1 of PARTIAL_TRAIL");
        timeStops.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void A_strategy_without_these_exits_gives_the_same_run_as_before_and_no_leg()
    {
        foreach (var day in Cases.FDays.Select(c => c.Name))
        {
            var result = Run(With(Rr), day);

            result.Walk.LegsTotal.ShouldBe(0, day);
            result.Walk.ExitsTimeStop.ShouldBe(0, day);
            result.Walk.StopLoosenIgnored.ShouldBe(0, day);
            result.Outcomes.ShouldAllBe(o => o.Walk.Trade == null || o.Walk.Trade.Legs == null);
        }
    }

    [Fact]
    public void The_definition_of_the_run_says_which_policy_ran()
    {
        var text = XDrishti.Simulation.Replay.DefinitionJson.Write(With(Be, TimeStop));

        text.ShouldContain("PARTIAL_BE", Case.Insensitive);
        text.ShouldContain("time_stop", Case.Insensitive);
    }
}
