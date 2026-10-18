using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>
/// Criterion 14 of P3-03a (the scanner's path): no look-ahead through the loop. For 100 seeded series and a cut <c>k</c> (a random one, the decision bar of a candidate and the bar before it), the candidates with
/// <c>DecisionBarIndex ≤ k</c> (trace included) and the counters as of step <c>k</c> are equal to those of the full run when the bars after <c>k</c> are removed and when they are replaced by arbitrary values. The scanner
/// is a pure stepper, so only a doctored input can show a leak; the negative control feeds it a <b>peeking feed</b>, in which bar <i>i</i> carries the open, high, low, close and volume of bar <i>i</i>+1 under its own epoch,
/// and the same property reports it on at least 10 seeds while the honest run reports none on the very same inputs.
/// </summary>
public sealed class LoopLookAheadPropertyTests
{
    private static readonly ulong[] Seeds = [.. Enumerable.Range(1, 100).Select(i => (ulong)i)];

    private static readonly (string Name, StrategyDefinition Definition)[] Strategies =
    [
        ("ORB", OrbV1.Create()),
        ("F19 invalidate", TestStrategies.InvalidateOnLowUnderRange()),
        ("F20 setup", TestStrategies.SetupAfterTen()),
        ("F21 per-session", TestStrategies.CrossVwap()),
    ];

    /// <summary>What a scanner has said by step <c>k</c>: the candidates made at or before it, with traces, and the counters at that moment.</summary>
    private sealed record Snapshot(string[] Candidates, string Counters);

    private static Snapshot Capture(EntryScanner scanner, int k) => new(
        [.. scanner.Candidates.Where(c => c.DecisionBarIndex <= k).Select(c => Render.Candidate(c))],
        Render.Counters(scanner.Counters));

    /// <summary>Steps the bars through a new scanner and takes the snapshot after step <paramref name="k"/> (the whole list is stepped).</summary>
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

    /// <summary>The bars after <paramref name="k"/> replaced by zero and huge prices and volumes, the epochs unchanged.</summary>
    private static Bar[] Replaced(Bar[] bars, int k, ulong seed)
    {
        var rng = new SeededSeries(seed ^ 0xABCDEF);
        var copy = (Bar[])bars.Clone();
        for (var i = k + 1; i < copy.Length; i++)
        {
            copy[i] = rng.Int(4) switch
            {
                0 => new Bar(copy[i].EpochSeconds, 0, 0, 0, 0, 0),
                1 => new Bar(copy[i].EpochSeconds, 1e12, 1e12, 1e12, 1e12, long.MaxValue / 4),
                2 => new Bar(copy[i].EpochSeconds, 1e12, 1e12, 0, 0.25, 0),
                _ => new Bar(copy[i].EpochSeconds, 0.25, 1e9, 0.25, 1e9, 1),
            };
        }

        return copy;
    }

    /// <summary>The peeking feed: bar <i>i</i> gets the open, high, low, close and volume of bar <i>i</i>+1 with its own epoch; the last bar keeps its own.</summary>
    private static Bar[] Peeking(Bar[] bars)
    {
        var result = new Bar[bars.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var source = i + 1 < bars.Length ? bars[i + 1] : bars[i];
            result[i] = new Bar(bars[i].EpochSeconds, source.Open, source.High, source.Low, source.Close, source.Volume);
        }

        return result;
    }

    /// <summary>The cuts of a seed: a random one, and for the first candidate of the full run its own bar and the bar before it.</summary>
    private static List<int> Cuts(ulong seed, int length, IReadOnlyList<EntryCandidate> candidates)
    {
        var rng = new SeededSeries(seed * 7919);
        var cuts = new List<int> { 1 + rng.Int(length - 2) };
        if (candidates.Count > 0)
        {
            var at = candidates[rng.Int(candidates.Count)].DecisionBarIndex;
            cuts.Add(at);
            cuts.Add(at - 1);
        }

        return cuts;
    }

    /// <summary>Whether the property is violated for this seed, side and strategy, with the feed transformed by <paramref name="feed"/> (the identity for the honest run).</summary>
    private static bool Violated(CompiledSide side, ulong seed, Func<Bar[], Bar[]> feed, out int candidates)
    {
        var bars = SeededSeries.Of(seed);
        var fullFeed = feed(bars);
        var full = Scan.Run(side, fullFeed);
        candidates = full.Candidates.Count;
        var violated = false;
        foreach (var k in Cuts(seed, bars.Length, full.Candidates))
        {
            var expected = RunTo(side, fullFeed, k);
            var removed = RunTo(side, feed([.. bars.Take(k + 1)]), k);
            var replaced = RunTo(side, feed(Replaced(bars, k, seed)), k);

            violated |= !Same(expected, removed) || !Same(expected, replaced);
        }

        return violated;
    }

    private static bool Same(Snapshot a, Snapshot b) => a.Counters == b.Counters && a.Candidates.SequenceEqual(b.Candidates);

    [Fact]
    public void The_generator_makes_enough_candidates_on_both_sides_for_the_property_to_mean_something()
    {
        var buy = CompiledOrb(Side.Buy);
        var sell = CompiledOrb(Side.Sell);

        var buys = Seeds.Sum(s => Scan.Run(buy, SeededSeries.Of(s)).Candidates.Count);
        var sells = Seeds.Sum(s => Scan.Run(sell, SeededSeries.Of(s)).Candidates.Count);

        (buys + sells).ShouldBeGreaterThanOrEqualTo(60);
        buys.ShouldBeGreaterThanOrEqualTo(20);
        sells.ShouldBeGreaterThanOrEqualTo(20);
    }

    private static CompiledSide CompiledOrb(Side side) => Scan.Compile(OrbV1.Create(), side);

    [Fact]
    public void The_honest_loop_reports_no_violation_on_any_seed_for_any_strategy_and_side()
    {
        var violations = new List<string>();
        foreach (var (name, definition) in Strategies)
        {
            foreach (var side in definition.Sides)
            {
                var compiled = Scan.Compile(definition, side);
                violations.AddRange(Seeds.Where(seed => Violated(compiled, seed, bars => bars, out _)).Select(seed => name + " " + side + " seed " + seed));
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void A_cut_at_the_bar_of_a_candidate_keeps_it_and_a_cut_one_bar_before_removes_it()
    {
        var kept = 0;
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var compiled = CompiledOrb(side);
            foreach (var seed in Seeds)
            {
                var bars = SeededSeries.Of(seed);
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

        kept.ShouldBeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public void The_peeking_feed_is_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none_on_the_same_inputs()
    {
        var reported = 0;
        var honest = 0;
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var compiled = CompiledOrb(side);
            foreach (var seed in Seeds)
            {
                reported += Violated(compiled, seed, Peeking, out _) ? 1 : 0;
                honest += Violated(compiled, seed, bars => bars, out _) ? 1 : 0;
            }
        }

        honest.ShouldBe(0);
        reported.ShouldBeGreaterThanOrEqualTo(10);
    }
}
