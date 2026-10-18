using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 21: the properties over the 500 seeded days are not vacuous. Every rule of the table has to fire at least ten times in the walks the properties compare, both sides and the three ticks have to occur, and a generator
/// that stops producing a case fails here and not silently in the properties.
/// </summary>
public sealed class SeededDaysTests
{
    private const int AtLeast = 10;

    [Fact]
    public void The_generator_is_deterministic_and_its_days_are_ascending_regular_sessions_with_candidates_on_both_sides()
    {
        foreach (var day in SeededDays.All)
        {
            var again = SeededDays.Make(day.Seed);

            again.Bars.ShouldBe(day.Bars, "seed " + day.Seed);
            again.Walks.Select(w => w.Candidate).ShouldBe(day.Walks.Select(w => w.Candidate), "seed " + day.Seed);
            day.Bars.Zip(day.Bars.Skip(1), (a, b) => b.EpochSeconds > a.EpochSeconds).ShouldAllBe(x => x, "seed " + day.Seed);
            day.Bars.ShouldAllBe(b => b.Low <= b.Open && b.Low <= b.Close && b.High >= b.Open && b.High >= b.Close && b.Low > 0, "seed " + day.Seed);
            day.Walks.Count.ShouldBeInRange(2, 7);
        }

        SeededDays.All.Count.ShouldBe(SeededDays.Count);
        SeededDays.All.SelectMany(d => d.Walks).Count(w => w.Candidate.Side == Side.Buy).ShouldBeGreaterThanOrEqualTo(500);
        SeededDays.All.SelectMany(d => d.Walks).Count(w => w.Candidate.Side == Side.Sell).ShouldBeGreaterThanOrEqualTo(500);
        SeededDays.All.Select(d => d.Tick).Distinct().Order().ShouldBe([0.01m, 0.05m, 0.10m]);
    }

    [Fact]
    public void Every_counter_of_the_table_is_hit_at_least_ten_times_over_the_500_days()
    {
        var walks = WalkProperties.WalkAll(SeededDays.All, FillRules.Default);
        var total = walks.Aggregate(WalkCounters.Empty, (sum, w) => sum.Add(w.Outcome.ToCounters()));

        TestContext.Current.TestOutputHelper?.WriteLine(OutcomeFormatter.Render(total));
        total.EntriesAtOpen.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.EntriesAtOpen));
        total.EntriesAtLimit.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.EntriesAtLimit));
        total.EntriesAfterMissingMinutes.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.EntriesAfterMissingMinutes));
        total.LimitNotReached.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.LimitNotReached));
        total.NoFillBar.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.NoFillBar));
        total.AfterSquareOff.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.AfterSquareOff));
        total.EntryThroughStop.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.EntryThroughStop));
        total.StopGapExits.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.StopGapExits));
        total.TargetGapExits.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.TargetGapExits));
        total.AmbiguousMinutes.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.AmbiguousMinutes));
        total.LateSquareOffs.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.LateSquareOffs));
        total.ZeroVolumeMinutes.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ZeroVolumeMinutes));
        total.RiskUnderFourTicks.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.RiskUnderFourTicks));
        total.ExitsEndOfData.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ExitsEndOfData));
        total.ExitsStop.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ExitsStop));
        total.ExitsTarget.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ExitsTarget));
        total.ExitsSquareOff.ShouldBeGreaterThanOrEqualTo(AtLeast, nameof(total.ExitsSquareOff));
    }

    [Fact]
    public void Both_sides_and_the_three_ticks_occur_among_the_trades()
    {
        var trades = WalkProperties.WalkAll(SeededDays.All, FillRules.Default).Where(w => w.Trade is not null).ToList();

        trades.Count(t => t.Trade!.Side == Side.Buy).ShouldBeGreaterThanOrEqualTo(AtLeast);
        trades.Count(t => t.Trade!.Side == Side.Sell).ShouldBeGreaterThanOrEqualTo(AtLeast);
        foreach (var tick in new[] { 0.01m, 0.05m, 0.10m })
        {
            trades.Count(t => t.Day.Tick == tick).ShouldBeGreaterThanOrEqualTo(AtLeast, "tick " + tick);
        }

        foreach (var reason in Enum.GetValues<ExitReason>().Where(r => r != ExitReason.MaxHold))
        {
            trades.Count(t => t.Trade!.ExitReason == reason && t.Trade.Side == Side.Buy).ShouldBeGreaterThanOrEqualTo(AtLeast / 2, reason + " buy");
            trades.Count(t => t.Trade!.ExitReason == reason && t.Trade.Side == Side.Sell).ShouldBeGreaterThanOrEqualTo(AtLeast / 2, reason + " sell");
        }
    }

    [Fact]
    public void The_identities_of_the_counters_hold_over_the_500_days()
    {
        foreach (var walk in WalkProperties.WalkAll(SeededDays.All, FillRules.Default))
        {
            var c = walk.Outcome.ToCounters();

            c.Candidates.ShouldBe(c.Trades + c.NoFillBar + c.AfterSquareOff + c.EntryThroughStop + c.LimitNotReached + c.PositionOpen, "seed " + walk.Day.Seed);
            c.Trades.ShouldBe(c.EntriesAtOpen + c.EntriesAtLimit, "seed " + walk.Day.Seed);
            c.Trades.ShouldBe(c.ExitsStop + c.ExitsTarget + c.ExitsSquareOff + c.ExitsMaxHold + c.ExitsEndOfData, "seed " + walk.Day.Seed);
            (walk.Outcome.NoTrade == NoTradeReason.LimitNotReached ? walk.Outcome.BarsExamined : 1).ShouldBe(1, "seed " + walk.Day.Seed);
        }
    }

    [Fact]
    public void Two_walks_of_the_same_input_one_on_a_copy_of_the_minutes_render_identical_text()
    {
        foreach (var day in SeededDays.All)
        {
            foreach (var walk in day.Walks)
            {
                var first = WalkProperties.Render(WalkProperties.Run(walk, day.Bars, FillRules.Default));
                var second = WalkProperties.Render(WalkProperties.Run(walk, [.. day.Bars], FillRules.Default));

                second.ShouldBe(first, "seed " + day.Seed);
            }
        }
    }
}
