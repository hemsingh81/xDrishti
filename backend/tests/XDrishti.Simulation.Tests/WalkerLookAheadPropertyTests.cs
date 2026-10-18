using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// AC8: no look-ahead through the walker. 100 seeded series; for a random cut <c>k</c> (and at the cut of the negative control, and at the minute before the exit of every trade) every outcome settled at an index at most <c>k</c> is the same when the minutes after <c>k</c> are removed and when they are replaced
/// by extreme values. The negative control runs the very same property on a doctored input that peeks one minute ahead and must report violations on at least 10 seeds.
/// </summary>
public sealed class WalkerLookAheadPropertyTests
{
    /// <summary>The seeds, written down: 1 to 100.</summary>
    private static readonly int[] Seeds = [.. Enumerable.Range(1, 100)];

    private static readonly List<SeededRun> Runs = [.. Seeds.Select(SeededRuns.Make)];

    [Fact]
    public void The_generator_makes_enough_compared_outcomes_of_every_kind_for_the_property_to_mean_something()
    {
        // The outcomes that the property compares: those settled at or before the cut of their series.
        var outcomes = Runs.SelectMany(r => LookAhead.FullRun(r, LookAhead.Identity).Where(o => o.Settled <= LookAhead.CutPoint(r))).ToList();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"compared {outcomes.Count}; exits {string.Join(", ", Enum.GetValues<ExitReason>().Select(r => r + " " + outcomes.Count(o => o.Outcome.Trade?.ExitReason == r)))}; no-trade {string.Join(", ", Enum.GetValues<NoTradeReason>().Select(r => r + " " + outcomes.Count(o => o.Outcome.NoTrade == r)))}");
        outcomes.Count.ShouldBeGreaterThanOrEqualTo(300);
        outcomes.Count(o => o.Walk.Candidate.Side == Side.Buy).ShouldBeGreaterThanOrEqualTo(50);
        outcomes.Count(o => o.Walk.Candidate.Side == Side.Sell).ShouldBeGreaterThanOrEqualTo(50);
        foreach (var reason in Enum.GetValues<ExitReason>())
        {
            outcomes.Count(o => o.Outcome.Trade?.ExitReason == reason).ShouldBeGreaterThanOrEqualTo(5, reason.ToString());
        }

        foreach (var reason in Enum.GetValues<NoTradeReason>())
        {
            outcomes.Count(o => o.Outcome.NoTrade == reason).ShouldBeGreaterThanOrEqualTo(5, reason.ToString());
        }

        outcomes.Count(o => o.Outcome.StopGapExit).ShouldBeGreaterThan(0);
        outcomes.Count(o => o.Outcome.EntryAfterMissingMinutes).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void An_outcome_settled_before_the_cut_does_not_change_when_the_minutes_after_it_are_removed_or_replaced()
    {
        var violations = new List<string>();
        var compared = 0;
        foreach (var run in Runs)
        {
            foreach (var cut in LookAhead.HonestCuts(run))
            {
                var (found, count) = LookAhead.Check(run, cut, LookAhead.Identity);
                violations.AddRange(found);
                compared += count;
            }
        }

        violations.ShouldBeEmpty();
        compared.ShouldBeGreaterThanOrEqualTo(300, "the property must compare outcomes, not skip them all");
    }

    [Fact]
    public void A_walker_that_peeks_one_minute_ahead_is_caught_on_at_least_ten_seeds_while_the_honest_one_is_not()
    {
        var peekingSeeds = Runs.Count(run => LookAhead.Check(run, LookAhead.PeekCutPoint(run), LookAhead.Peek).Violations.Count > 0);
        var honestSeeds = Runs.Count(run => LookAhead.Check(run, LookAhead.PeekCutPoint(run), LookAhead.Identity).Violations.Count > 0);

        TestContext.Current.TestOutputHelper?.WriteLine($"the peeking input is caught on {peekingSeeds} of {Runs.Count} seeds");
        peekingSeeds.ShouldBeGreaterThanOrEqualTo(10);
        honestSeeds.ShouldBe(0, "the honest walker on the very same inputs");
    }

    [Fact]
    public void The_replacement_really_changes_the_minutes_after_the_cut_and_keeps_the_epochs()
    {
        var run = Runs[0];
        var k = run.Bars.Count / 2;

        var replaced = LookAhead.ReplaceAfter(run.Bars, k);

        replaced.Count.ShouldBe(run.Bars.Count);
        replaced.Take(k + 1).ShouldBe(run.Bars.Take(k + 1));
        replaced.Skip(k + 1).Select(b => b.EpochSeconds).ShouldBe(run.Bars.Skip(k + 1).Select(b => b.EpochSeconds));
        replaced.Skip(k + 1).ShouldAllBe(b => b.Volume == 0 || b.Volume == long.MaxValue);
    }

    [Fact]
    public void The_seeded_series_are_ascending_regular_weekdays_of_one_to_three_sessions()
    {
        foreach (var run in Runs)
        {
            run.Bars.Zip(run.Bars.Skip(1), (a, b) => b.EpochSeconds > a.EpochSeconds).ShouldAllBe(x => x);
            var dates = run.Bars.Select(b => SessionClock.DateOf(b.EpochSeconds)).Distinct().ToList();
            dates.Count.ShouldBeInRange(1, 3);
            dates.ShouldAllBe(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday);
            run.Walks.Count.ShouldBeInRange(2, 6);
        }
    }
}
