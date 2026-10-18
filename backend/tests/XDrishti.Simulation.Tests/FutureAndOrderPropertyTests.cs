using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 20: (a) the order inside an ambiguous minute never improves a trade; (b) a closed trade does not depend on the future (P3-03's AC8 with the rules of this slice, with the default and with no slippage); (c) the negative
/// controls: a walker that peeks one minute ahead, and one that decides one minute early, are caught on at least ten seeds while the honest walker is not, on the very same inputs.
/// </summary>
public sealed class FutureAndOrderPropertyTests
{
    private static readonly FillRules None = FillRules.Create(0.1, SlippageConfig.None).Value;

    private static readonly FillRules[] Rules = [FillRules.Default, None];

    // (a) the order inside an ambiguous minute

    [Fact]
    public void The_unsplit_ambiguous_minute_equals_the_worse_of_the_two_orders_its_path_could_have_taken()
    {
        var compared = 0;
        foreach (var walk in WalkProperties.WalkAll(SeededDays.All, FillRules.Default).Where(w => w.Outcome.AmbiguousMinute))
        {
            var trade = walk.Trade!;
            var bars = walk.Day.Bars;
            var index = WalkProperties.IndexOf(bars, trade.ExitEpochSeconds, walk.Walk.Candidate.DecisionBarIndex + 1);
            var minute = bars[index];

            var highFirst = Split(bars, index, [new Bar(minute.EpochSeconds, minute.Open, minute.High, minute.Open, minute.High, minute.Volume / 2), new Bar(minute.EpochSeconds + 30, minute.High, minute.High, minute.Low, minute.Close, minute.Volume / 2)]);
            var lowFirst = Split(bars, index, [new Bar(minute.EpochSeconds, minute.Open, minute.Open, minute.Low, minute.Low, minute.Volume / 2), new Bar(minute.EpochSeconds + 30, minute.Low, minute.High, minute.Low, minute.Close, minute.Volume / 2)]);
            var a = WalkProperties.Ok(WalkProperties.Run(walk.Walk, highFirst, FillRules.Default)).Trade.ShouldNotBeNull();
            var b = WalkProperties.Ok(WalkProperties.Run(walk.Walk, lowFirst, FillRules.Default)).Trade.ShouldNotBeNull();
            var worse = a.RExact <= b.RExact ? a : b;
            var label = $"seed {walk.Day.Seed} {trade.Side}";

            trade.RExact.ShouldBe(worse.RExact, 1e-12, label);
            trade.ExitReason.ShouldBe(worse.ExitReason, label);
            trade.ExitReason.ShouldBe(ExitReason.Stop, label + ": an ambiguous minute is a stop");
            Math.Max(a.RExact, b.RExact).ShouldBeGreaterThan(trade.RExact, label + ": the other order was better, so the split is not vacuous");
            compared++;
        }

        compared.ShouldBeGreaterThanOrEqualTo(10);
    }

    private static List<Bar> Split(IReadOnlyList<Bar> bars, int index, Bar[] halves)
    {
        var result = new List<Bar>(bars.Count + 1);
        result.AddRange(bars.Take(index));
        result.AddRange(halves);
        result.AddRange(bars.Skip(index + 1));
        return result;
    }

    // (b) a closed trade does not depend on the future

    [Fact]
    public void An_outcome_settled_before_the_cut_does_not_change_when_the_minutes_after_it_are_removed_or_replaced()
    {
        var compared = 0;
        var violations = new List<string>();
        foreach (var rules in Rules)
        {
            foreach (var day in SeededDays.All)
            {
                foreach (var cut in HonestCuts(day, rules))
                {
                    var (found, count) = Check(day, cut, rules, WalkProperties.Identity);
                    violations.AddRange(found);
                    compared += count;
                }
            }
        }

        violations.ShouldBeEmpty();
        compared.ShouldBeGreaterThanOrEqualTo(5_000, "the property must compare outcomes, not skip them all");
    }

    [Fact]
    public void The_replacement_really_changes_the_minutes_after_the_cut_and_keeps_the_epochs()
    {
        var day = SeededDays.All[0];
        var k = day.Bars.Count / 2;

        var replaced = WalkProperties.ReplaceAfter(day.Bars, k);

        replaced.Count.ShouldBe(day.Bars.Count);
        replaced.Take(k + 1).ShouldBe(day.Bars.Take(k + 1));
        replaced.Skip(k + 1).Select(b => b.EpochSeconds).ShouldBe(day.Bars.Skip(k + 1).Select(b => b.EpochSeconds));
        replaced.Skip(k + 1).ShouldAllBe(b => b.Volume == 0 || b.Volume == long.MaxValue);
    }

    // (c) the negative controls

    [Fact]
    public void A_walker_that_peeks_one_minute_ahead_is_caught_on_at_least_ten_seeds_while_the_honest_walker_is_not()
    {
        var peeking = 0;
        var honest = 0;
        foreach (var day in SeededDays.All)
        {
            peeking += Differs(day, WalkProperties.Peek) ? 1 : 0;
            honest += Differs(day, WalkProperties.Identity) ? 1 : 0;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"the peeking input is caught on {peeking} of {SeededDays.All.Count} seeds");
        peeking.ShouldBeGreaterThanOrEqualTo(10);
        honest.ShouldBe(0, "the honest walker on the very same inputs");
    }

    [Fact]
    public void A_walker_that_decides_one_minute_early_breaks_the_fill_timing_check_of_criterion_19_on_at_least_ten_seeds_while_the_honest_one_never_does()
    {
        var early = 0;
        var honest = 0;
        foreach (var day in SeededDays.All)
        {
            early += day.Walks.Any(w => BreaksCriterion19(day, w, WalkProperties.OneMinuteEarlier(w.Candidate, day.Bars))) ? 1 : 0;
            honest += day.Walks.Any(w => BreaksCriterion19(day, w, w.Candidate)) ? 1 : 0;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"the early decision is caught on {early} of {SeededDays.All.Count} seeds");
        early.ShouldBeGreaterThanOrEqualTo(10);
        honest.ShouldBe(0);
    }

    /// <summary>
    /// The walk of <paramref name="candidate"/> (possibly doctored) breaks criterion 19 measured against the instant the series really decided: the whole check of <see cref="FillTimingCheck"/> (fill minute at or after the
    /// true decision instant, the first minute that holds a trade, ...), with the true candidate as the truth.
    /// </summary>
    private static bool BreaksCriterion19(SeededDay day, SeededWalk walk, EntryCandidate candidate)
    {
        var result = WalkProperties.Run(walk with { Candidate = candidate }, day.Bars, FillRules.Default);
        return result.IsSuccess && FillTimingCheck.Violations(day, walk.Candidate, result.Value, FillRules.Default).Count > 0;
    }

    /// <summary>
    /// The control of (b): <c>f(X)</c> is the walker applied to the doctored minutes; it is compared with <c>f(truncate(X))</c>, the list cut right after the exit minute (or the deciding minute) that <c>f(X)</c> reports.
    /// True when any walk of the day differs.
    /// </summary>
    private static bool Differs(SeededDay day, Func<IReadOnlyList<Bar>, Side, IReadOnlyList<Bar>> doctor)
    {
        foreach (var walk in day.Walks)
        {
            var side = walk.Candidate.Side;
            var full = WalkProperties.Run(walk, doctor(day.Bars, side), FillRules.Default);
            var outcome = WalkProperties.Ok(full);
            var settled = WalkProperties.SettledAt(walk.Candidate, day.Bars, outcome);
            var truncated = WalkProperties.Run(walk, doctor([.. day.Bars.Take(settled + 1)], side), FillRules.Default);

            if (WalkProperties.Render(full) != WalkProperties.Render(truncated))
            {
                return true;
            }
        }

        return false;
    }

    // the harness of (b)

    /// <summary>The cuts of a day: a random one in the last tenth, and for every walk the minute before the minute it was settled at (where a walker that looked ahead would have decided earlier).</summary>
    private static List<int> HonestCuts(SeededDay day, FillRules rules)
    {
        var rng = new SeededSeries(((ulong)day.Seed * 104_729UL) + 7UL);
        var from = day.Bars.Count * 9 / 10;
        var cuts = new List<int> { from + rng.Int(Math.Max(1, day.Bars.Count - from)) };
        foreach (var walk in day.Walks)
        {
            var settled = WalkProperties.SettledAt(walk.Candidate, day.Bars, WalkProperties.Ok(WalkProperties.Run(walk, day.Bars, rules)));
            if (settled > 0)
            {
                cuts.Add(settled - 1);
            }
        }

        return [.. cuts.Distinct()];
    }

    /// <summary>
    /// For the cut <paramref name="k"/>: every outcome of the full day that is settled at an index at most <paramref name="k"/> equals, as rendered, the same walk over the list cut there (removed) and over the list whose
    /// minutes after it are replaced by extreme values. Returns the violations and how many outcomes were compared.
    /// </summary>
    private static (List<string> Violations, int Compared) Check(SeededDay day, int k, FillRules rules, Func<IReadOnlyList<Bar>, Side, IReadOnlyList<Bar>> doctor)
    {
        var violations = new List<string>();
        var compared = 0;
        var removed = day.Bars.Take(k + 1).ToList();
        var replaced = WalkProperties.ReplaceAfter(day.Bars, k);
        foreach (var walk in day.Walks)
        {
            var side = walk.Candidate.Side;
            var full = WalkProperties.Run(walk, doctor(day.Bars, side), rules);
            if (WalkProperties.SettledAt(walk.Candidate, day.Bars, WalkProperties.Ok(full)) > k)
            {
                continue;
            }

            compared++;
            var expected = WalkProperties.Render(full);
            foreach (var (name, list) in new[] { ("removed", (IReadOnlyList<Bar>)removed), ("replaced", replaced) })
            {
                var cut = WalkProperties.Render(WalkProperties.Run(walk, doctor(list, side), rules));
                if (cut != expected)
                {
                    violations.Add($"seed {day.Seed} k {k} candidate {walk.Candidate.DecisionBarIndex} {name}: {expected} <> {cut}");
                }
            }
        }

        return (violations, compared);
    }
}
