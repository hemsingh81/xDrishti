using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>
/// Criterion 13 of P3-05, the scanner's path of the look-ahead property of P3-03a (criterion 14) for <c>VWAP_PULLBACK</c> on 100 seeded series (a banded prefix of 21 sessions and one to three random days, the generator of D12): for a cut <c>k</c> (a
/// random one, the decision bar of a candidate and the bar before it) the candidates with <c>DecisionBarIndex ≤ k</c>, trace included, and the counters as of step <c>k</c> are equal to those of the full run when the bars after <c>k</c> are removed and when
/// they are replaced by arbitrary values (zero and huge prices and volumes); the replacement reaches the current session's minutes after the cut, which feed the forming <c>1h</c> bar, so the <c>tf("1h")</c> and <c>tf("1D")</c> reads at the cut are shown to
/// depend on closed bars only. The negative controls are doctored feeds, each reported on at least 10 seeds while the honest run reports none on the very same inputs: the peeking feed of P3-03a, and a feed that rewrites every minute of an hourly bucket
/// with the last minute of that bucket (the forming hourly bar has its final value early).
/// </summary>
public sealed class VwapLookAheadPropertyTests
{
    private static readonly ulong[] Seeds = [.. Enumerable.Range(1, 100).Select(i => (ulong)i)];

    private sealed record Snapshot(string[] Candidates, string Counters);

    private static CompiledSide Compiled(Side side, ulong seed) => Scan.Compile(VwapPullback.Create(VwapSeeded.PullAtr(seed)), side);

    private static Snapshot Capture(EntryScanner scanner, int k) => new(
        [.. scanner.Candidates.Where(c => c.DecisionBarIndex <= k).Select(c => Render.Candidate(c))],
        Render.Counters(scanner.Counters));

    private static Snapshot RunTo(CompiledSide side, Bar[] bars, int k)
    {
        var scanner = Scan.Scanner(side);
        Snapshot? snapshot = null;
        for (var i = 0; i < bars.Length; i++)
        {
            scanner.Step(bars[i]);
            if (i == k)
            {
                snapshot = Capture(scanner, k);
            }
        }

        return snapshot ?? throw new InvalidOperationException("The cut is outside the list.");
    }

    private static List<int> Cuts(ulong seed, int length, IReadOnlyList<EntryCandidate> candidates)
    {
        var rng = new SeededSeries(seed * 7919);
        var cuts = new List<int> { VwapSeeded.PrefixMinutes + rng.Int(length - VwapSeeded.PrefixMinutes - 2) };
        if (candidates.Count > 0)
        {
            var at = candidates[rng.Int(candidates.Count)].DecisionBarIndex;
            cuts.Add(at);
            cuts.Add(at - 1);
        }

        return cuts;
    }

    private static bool Violated(CompiledSide side, ulong seed, Func<Bar[], Bar[]> feed)
    {
        var bars = VwapSeeded.Of(seed);
        var fullFeed = feed(bars);
        var full = Scan.Run(side, fullFeed);
        var violated = false;
        foreach (var k in Cuts(seed, bars.Length, full.Candidates))
        {
            var expected = RunTo(side, fullFeed, k);
            var removed = RunTo(side, feed([.. bars.Take(k + 1)]), k);
            var replaced = RunTo(side, feed(Feeds.Replaced(bars, k, seed)), k);

            violated |= !Same(expected, removed) || !Same(expected, replaced);
        }

        return violated;
    }

    private static bool Same(Snapshot a, Snapshot b) => a.Counters == b.Counters && a.Candidates.SequenceEqual(b.Candidates);

    /// <summary>The hourly peeking control: every minute of an hourly bucket (09:15, 10:15 ... 14:15 for 60 minutes, 15:15 for 15) gets the open, high, low, close and volume of the last minute of that bucket under its own epoch.</summary>
    private static Bar[] HourlyFinalEarly(Bar[] bars)
    {
        var result = (Bar[])bars.Clone();
        var i = 0;
        while (i < bars.Length)
        {
            var bucket = BucketOf(bars[i].EpochSeconds);
            var j = i;
            while (j + 1 < bars.Length && BucketOf(bars[j + 1].EpochSeconds) == bucket)
            {
                j++;
            }

            for (var m = i; m < j; m++)
            {
                result[m] = new Bar(bars[m].EpochSeconds, bars[j].Open, bars[j].High, bars[j].Low, bars[j].Close, bars[j].Volume);
            }

            i = j + 1;
        }

        return result;
    }

    private static (long Day, long Hour) BucketOf(long epoch)
    {
        var local = epoch + 330 * 60L;
        return (local / 86_400, (local % 86_400 / 60 - (9 * 60 + 15)) / 60);
    }

    [Fact]
    public void The_generator_makes_enough_candidates_on_both_sides_for_the_property_to_mean_something()
    {
        var buys = Seeds.Sum(s => Scan.Run(Compiled(Side.Buy, s), VwapSeeded.Of(s)).Candidates.Count);
        var sells = Seeds.Sum(s => Scan.Run(Compiled(Side.Sell, s), VwapSeeded.Of(s)).Candidates.Count);

        var seen = $"buys {buys}, sells {sells}";
        (buys + sells).ShouldBeGreaterThanOrEqualTo(60, seen);
        buys.ShouldBeGreaterThanOrEqualTo(20, seen);
        sells.ShouldBeGreaterThanOrEqualTo(20, seen);
    }

    [Fact]
    public void The_honest_loop_reports_no_violation_on_any_seed_for_either_side()
    {
        var violations = new List<string>();
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            violations.AddRange(Seeds.Where(seed => Violated(Compiled(side, seed), seed, bars => bars)).Select(seed => side + " seed " + seed));
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void A_cut_at_the_bar_of_a_candidate_keeps_it_and_a_cut_one_bar_before_removes_it()
    {
        var kept = 0;
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            foreach (var seed in Seeds)
            {
                var compiled = Compiled(side, seed);
                var bars = VwapSeeded.Of(seed);
                var full = Scan.Run(compiled, bars);
                if (full.Candidates.Count == 0)
                {
                    continue;
                }

                var at = full.Candidates[0].DecisionBarIndex;
                RunTo(compiled, [.. bars.Take(at + 1)], at).Candidates.Length.ShouldBeGreaterThan(0, "seed " + seed);
                RunTo(compiled, [.. bars.Take(at)], at - 1).Candidates.ShouldBeEmpty("seed " + seed);
                kept++;
            }
        }

        kept.ShouldBeGreaterThanOrEqualTo(20, "seeds with a candidate " + kept);
    }

    [Fact]
    public void The_peeking_feed_is_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none_on_the_same_inputs()
    {
        var (reported, honest) = Count(Feeds.Peeking);

        honest.ShouldBe(0);
        reported.ShouldBeGreaterThanOrEqualTo(10, "seeds reported " + reported);
    }

    [Fact]
    public void The_feed_that_gives_an_hourly_bar_its_final_value_early_is_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none()
    {
        var (reported, honest) = Count(HourlyFinalEarly);

        honest.ShouldBe(0);
        reported.ShouldBeGreaterThanOrEqualTo(10, "seeds reported " + reported);
    }

    private static (int Reported, int Honest) Count(Func<Bar[], Bar[]> doctored)
    {
        var reported = 0;
        var honest = 0;
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            foreach (var seed in Seeds)
            {
                var compiled = Compiled(side, seed);
                reported += Violated(compiled, seed, doctored) ? 1 : 0;
                honest += Violated(compiled, seed, bars => bars) ? 1 : 0;
            }
        }

        return (reported, honest);
    }
}
