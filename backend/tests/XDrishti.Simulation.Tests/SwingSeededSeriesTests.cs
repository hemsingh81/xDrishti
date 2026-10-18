using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 15 and 24 of P3-03d: the seeded multi-day generator is deterministic and not vacuous. Every counter of the swing rows has to fire at least ten times over the 500 series, every hold from 1 to 5 sessions occurs with and without a
/// calendar, the three ticks occur, and a generator that stops producing a case fails here and not silently in the properties. The identities of D10 hold on every series.
/// </summary>
public sealed class SwingSeededSeriesTests
{
    private const int AtLeast = 10;

    [Fact]
    public void The_generator_is_deterministic_and_its_series_are_ascending_and_hold_candidates_the_walker_accepts()
    {
        foreach (var series in SeededSwing.All)
        {
            var again = SeededSwing.Make(series.Seed);

            again.Bars.ShouldBe(series.Bars, "seed " + series.Seed);
            again.Candidates.Select(c => (c.DecisionEpochSeconds, c.StopLevel, c.DecisionBarIndex)).ShouldBe(series.Candidates.Select(c => (c.DecisionEpochSeconds, c.StopLevel, c.DecisionBarIndex)), "seed " + series.Seed);
            series.Bars.Zip(series.Bars.Skip(1), (a, b) => b.EpochSeconds > a.EpochSeconds).ShouldAllBe(x => x, "seed " + series.Seed);
            series.Bars.ShouldAllBe(b => b.Low <= b.Open && b.Low <= b.Close && b.High >= b.Open && b.High >= b.Close && b.Low > 0, "seed " + series.Seed);
            series.Candidates.ShouldNotBeEmpty("seed " + series.Seed);
            series.Candidates.ShouldAllBe(c => series.Bars[c.DecisionBarIndex].EpochSeconds == c.DecisionEpochSeconds - 60, "seed " + series.Seed);
            series.Candidates.Zip(series.Candidates.Skip(1), (a, b) => a.DecisionEpochSeconds < b.DecisionEpochSeconds).ShouldAllBe(x => x, "seed " + series.Seed);
        }

        SeededSwing.All.Count.ShouldBe(SeededSwing.Count);
        SeededSwing.All.Select(s => s.Tick).Distinct().Order().ShouldBe([0.01m, 0.05m, 0.10m]);
        SeededSwing.All.Select(s => s.Sessions).Distinct().Order().ShouldBe([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void Every_hold_from_one_to_five_sessions_occurs_with_a_calendar_and_without_one()
    {
        foreach (var sessions in Enumerable.Range(1, 5))
        {
            SeededSwing.All.Count(s => s.Sessions == sessions && s.Given is not null).ShouldBeGreaterThanOrEqualTo(AtLeast, "N " + sessions + " with a calendar");
            SeededSwing.All.Count(s => s.Sessions == sessions && s.Given is null).ShouldBeGreaterThanOrEqualTo(AtLeast, "N " + sessions + " without");
        }

        SeededSwing.All.Count(s => s.Given is not null).ShouldBeInRange(300, 400, "about 70 % of the series are given the calendar");
    }

    [Fact]
    public void Every_counter_of_the_swing_rows_is_hit_at_least_ten_times_over_the_500_series()
    {
        var walks = SwingWalks.WalkAll(SeededSwing.All, FillRules.Default);
        var total = walks.Aggregate(WalkCounters.Empty, (sum, w) => sum.Add(w.Outcome.ToCounters()));
        var gated = SeededSwing.All.SelectMany(s => SwingWalks.Gated(s, FillRules.Default)).Aggregate(WalkCounters.Empty, (sum, o) => sum.Add(o.Walk.ToCounters()));
        var unseen = walks.Count(w => w.Trade is { SessionsUnseen: > 0 });

        TestContext.Current.TestOutputHelper?.WriteLine(OutcomeFormatter.Render(total));
        TestContext.Current.TestOutputHelper?.WriteLine("gated " + OutcomeFormatter.Render(gated) + $" | trades with unseen sessions {unseen} | candidates {walks.Count}");
        total.ExitsMaxHold.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ExitsMaxHold));
        total.MaxHoldAtLastClose.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.MaxHoldAtLastClose));
        total.OvernightStopGaps.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.OvernightStopGaps));
        total.OvernightTargetGaps.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.OvernightTargetGaps));
        total.StopGapExits.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.StopGapExits));
        total.TargetGapExits.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.TargetGapExits));
        total.ExitsEndOfData.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ExitsEndOfData));
        total.ExitsStop.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ExitsStop));
        total.ExitsTarget.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ExitsTarget));
        total.AmbiguousMinutes.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.AmbiguousMinutes));
        total.ZeroVolumeMinutes.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ZeroVolumeMinutes));
        gated.PositionOpen.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(gated.PositionOpen));
        unseen.ShouldBeGreaterThanOrEqualTo(AtLeast, "trades with unseen sessions");
        total.ExitsSquareOff.ShouldBe(0);
        total.SessionsHeld.ShouldBeGreaterThan(total.Trades, "some trade must be held more than one session");
    }

    [Fact]
    public void The_planted_cases_are_all_there_and_each_is_at_least_ten_series()
    {
        var tags = SeededSwing.All.SelectMany(s => s.Tags).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(", ", tags.OrderBy(t => t.Key).Select(t => t.Key + "=" + t.Value)));
        foreach (var tag in new[] { "night-beyond-stop", "night-beyond-target", "both-levels", "session-removed", "due-minute-missing", "due-minute-empty-with-candidates", "list-ends-in-hold" })
        {
            tags.GetValueOrDefault(tag).ShouldBeGreaterThanOrEqualTo(AtLeast, tag);
        }
    }

    [Fact]
    public void The_identities_of_the_counters_hold_on_every_walk_and_every_gated_series()
    {
        foreach (var walk in SwingWalks.WalkAll(SeededSwing.All, FillRules.Default))
        {
            walk.Result.IsSuccess.ShouldBeTrue("seed " + walk.Series.Seed);
            AssertIdentities(walk.Outcome.ToCounters(), "seed " + walk.Series.Seed);
        }

        foreach (var series in SeededSwing.All)
        {
            var sum = SwingWalks.Gated(series, FillRules.Default).Aggregate(WalkCounters.Empty, (acc, o) => acc.Add(o.Walk.ToCounters()));
            AssertIdentities(sum, "gated seed " + series.Seed);
            sum.Candidates.ShouldBe(series.Candidates.Count, "seed " + series.Seed);
        }
    }

    private static void AssertIdentities(WalkCounters c, string label)
    {
        c.Candidates.ShouldBe(c.Trades + c.NoFillBar + c.AfterSquareOff + c.EntryThroughStop + c.LimitNotReached + c.PositionOpen, label);
        c.Trades.ShouldBe(c.ExitsStop + c.ExitsTarget + c.ExitsSquareOff + c.ExitsMaxHold + c.ExitsEndOfData, label);
        c.Trades.ShouldBe(c.EntriesAtOpen + c.EntriesAtLimit, label);
        c.OvernightStopGaps.ShouldBeLessThanOrEqualTo(c.StopGapExits, label);
        c.OvernightTargetGaps.ShouldBeLessThanOrEqualTo(c.TargetGapExits, label);
        c.MaxHoldAtLastClose.ShouldBeLessThanOrEqualTo(c.ExitsMaxHold, label);
        c.SessionsHeld.ShouldBeGreaterThanOrEqualTo(c.Trades, label);
        c.ExitsSquareOff.ShouldBe(0, label);
        c.AfterSquareOff.ShouldBe(0, label);
        c.LateSquareOffs.ShouldBe(0, label);
    }

    [Fact]
    public void Two_walks_of_the_same_input_one_on_a_copy_render_identical_text_also_under_a_culture_with_comma_decimals()
    {
        var comma = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (var series in SeededSwing.All)
            {
                foreach (var candidate in series.Candidates)
                {
                    var rules = series.Rules(FillRules.Default);
                    var first = SwingWalks.Text(TradeWalker_Walk(candidate, series.Bars, rules));
                    System.Globalization.CultureInfo.CurrentCulture = comma;
                    var second = SwingWalks.Text(TradeWalker_Walk(candidate, [.. series.Bars], rules));
                    System.Globalization.CultureInfo.CurrentCulture = before;

                    second.ShouldBe(first, "seed " + series.Seed);
                }
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    private static Domain.Common.Result<Walking.WalkOutcome> TradeWalker_Walk(EntryCandidate candidate, IReadOnlyList<Domain.Market.Bars.Bar> bars, Walking.WalkRules rules) =>
        Walking.TradeWalker.Walk(candidate, bars, rules);
}
