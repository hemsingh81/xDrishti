using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>AC9: over the seeded runs of AC8, every trade fills where D3 says and its R, MAE and MFE equal a plain reference loop written in the test (<see cref="FillProperties"/>, shared with the simulator's AC16).</summary>
public sealed class FillTimingPropertyTests
{
    private static readonly List<SeededRun> Runs = [.. Enumerable.Range(1, 100).Select(SeededRuns.Make)];

    [Fact]
    public void Every_trade_fills_in_the_minute_after_the_candidate_within_the_delay_and_before_the_square_off()
    {
        var checkedTrades = 0;
        foreach (var run in Runs)
        {
            foreach (var (walk, outcome, _) in LookAhead.FullRun(run, LookAhead.Identity).Where(o => o.Outcome.Trade is not null))
            {
                FillProperties.AssertFill(outcome.Trade!, walk.Candidate, run.Bars, outcome, walk.Rules.SquareOffMinute);
                checkedTrades++;
            }
        }

        checkedTrades.ShouldBeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void Every_exit_price_lies_in_its_minute_or_is_the_open_or_the_close_the_rule_names()
    {
        foreach (var run in Runs)
        {
            foreach (var (walk, outcome, settled) in LookAhead.FullRun(run, LookAhead.Identity).Where(o => o.Outcome.Trade is not null))
            {
                FillProperties.AssertExit(outcome.Trade!, run.Bars, outcome, settled, walk.Candidate.DecisionBarIndex + 1, walk.Rules.SquareOffMinute, "seed " + run.Seed);
            }
        }
    }

    [Fact]
    public void R_and_the_excursions_equal_a_plain_reference_loop_and_the_exit_is_inside_them()
    {
        foreach (var run in Runs)
        {
            foreach (var (walk, outcome, settled) in LookAhead.FullRun(run, LookAhead.Identity).Where(o => o.Outcome.Trade is not null))
            {
                FillProperties.AssertExcursions(outcome.Trade!, run.Bars, outcome, settled, walk.Candidate.DecisionBarIndex + 1, "seed " + run.Seed);
            }
        }
    }
}
