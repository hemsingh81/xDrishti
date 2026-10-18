using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 28 of P3-03d: the named limits of D11 are pinned. A blocked signal spends the day: the scanner makes at most one candidate per date and side and does not know the gate, so a candidate at 10:45 that a position blocks
/// (the position leaves at 11:00) still spends the date, and a valid signal at 12:45 is a later trigger of the scanner and never a candidate. (The other limit, a trade that P3-03c skips for quantity still blocks, is pinned by G6 of the
/// slice that merges second.)
/// </summary>
public sealed class SwingNamedLimitsTests
{
    private static Bar[] Bars() => Day.Join(
        Day.Dense(Mon).Start(99.00).Set("09:20 99.00/100.00/99.00/99.00", "09:25 99.00/99.00/98.00/99.00", "10:30 99.50/101.50/99.50/101.50"),
        Day.Dense(Tue).Start(101.50).Set("10:30 101.50/103.00/101.50/103.00", "11:00 103.00/103.00/97.90/98.00", "12:15 98.00/102.50/98.00/102.50"));

    [Fact]
    public void A_candidate_that_the_gate_blocks_still_spends_the_day_so_a_later_valid_signal_is_not_a_candidate()
    {
        var definition = TestStrategies.Swing1h(5) with { Timeframes = [Timeframe.M30] };

        var result = Sim.Run(definition, Bars(), Timeframe.M30, calendar: DefaultWeek());

        result.Outcomes.Select(o => (o.Candidate.DecisionEpochSeconds, o.Walk.NoTrade)).ShouldBe(
        [
            (Day.Epoch(Mon, Day.Minute("10:45")), (NoTradeReason?)null),
            (Day.Epoch(Tue, Day.Minute("10:45")), NoTradeReason.PositionOpen),
        ]);
        result.Outcomes[0].Walk.Trade!.ExitEpochSeconds.ShouldBe(Day.Epoch(Tue, Day.Minute("11:00")));
        result.Outcomes[0].Walk.Trade!.ExitReason.ShouldBe(ExitReason.Stop);
        result.Walk.PositionOpen.ShouldBe(1);
        result.Scans[0].Candidates.ShouldBe(2);
        result.Scans[0].LaterTriggers.ShouldBeGreaterThanOrEqualTo(1, "the valid signal at 12:45 on Tuesday is a later trigger of the scanner, never a candidate");
        result.Outcomes.ShouldNotContain(o => o.Candidate.DecisionEpochSeconds == Day.Epoch(Tue, Day.Minute("12:45")));
    }

    [Fact]
    public void The_gate_blocks_the_candidate_even_though_the_signal_at_12_45_would_have_been_free_to_enter()
    {
        // The position has left at 11:00; an ideal gate that looked at the signal at 12:45 would let it in. The accepted limit: the scanner is exit-independent (the seam), so it never makes that candidate.
        var result = Sim.Run(TestStrategies.Swing1h(5) with { Timeframes = [Timeframe.M30] }, Bars(), Timeframe.M30, calendar: DefaultWeek());

        result.Trades.Count.ShouldBe(1);
        result.Walk.Trades.ShouldBe(1);
    }
}
