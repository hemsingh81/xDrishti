using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>What one run of the look-ahead property found.</summary>
internal sealed record LookAheadReport(IReadOnlyList<string> Violations, int Cuts, int Comparisons, IReadOnlyDictionary<Truth, int> Values)
{
    public int ViolationCount { get; init; } = Violations.Count;

    /// <summary>How many of the runs also had the context days from the cut date on removed or changed (0 for a scenario with no context records).</summary>
    public int ContextRuns { get; init; }
}

/// <summary>
/// The truncation and future-perturbation property (spec P3-02, criterion 10). For a series, the conditions are run through a feeder over the full series; then, for chosen cut points <i>c</i>, over (a) the bars up to
/// <i>c</i> only — every later 1-minute bar removed — and (b) the bars up to <i>c</i> followed by the later bars replaced by arbitrary values (extreme prices, zero volume, zero prices, huge volumes) at the same
/// times. At every step up to <i>c</i> at which the evaluation timeframe closed a bar, the three-valued result and the decision of every condition must be the same as in the full run: the future does not reach the
/// past. The cut points are the closed bars themselves (all of them when there are few, else a sample that always has the first and the last) and a few minutes in the middle of a bar.
/// <para>
/// <b>The market context is cut with the bars.</b> A frame at a step on the date <i>D</i> reads the context as of the latest day strictly before <i>D</i>, so the record dated <i>D</i> and every later one must not matter for
/// any step up to the cut (whose date is <i>D</i>): in the truncated run the records from <i>D</i> on are removed too, in the perturbed run they are replaced by changed ones (<see cref="OracleContext.Altered"/>: other prices, a
/// VIX of 99.9, a breadth of 3), exactly as the prefix property of the date lane in the Indicators tests does. A scenario that was given its records (<see cref="Scenario.ContextDays"/>) gets this; one without
/// context, or given a ready context, keeps its context unchanged in every run.
/// </para>
/// <para>
/// <b>What it guards, and what it cannot.</b> A compiled condition is a pure function of the frame as it is at the step, and the feeder gives the frame the bars up to the step before the condition is read, so the
/// answer at step <i>i</i> cannot depend on a later bar whatever the compiled code does. What the property can see is the <i>feed</i> and the <i>frame</i>: a feeder that reads after a later bar (the negative controls),
/// a frame whose closed values or calendar or context lookups depend on bars it has not been given, hidden or static state that makes a run differ from another, non-determinism. It cannot fail for a change in the
/// compiler (a wrong lag, a wrong clock) — those are guarded by the interpreter oracle, the hand examples and, for "reads only the past", the tests of the expression trees and the architecture test on the members
/// of the frame the library may use (<c>CompiledTreeTests</c>, <c>StrategiesArchitectureTests</c>).
/// </para>
/// </summary>
internal static class LookAheadProperty
{
    public static LookAheadReport Check(IFeeder feeder, Scenario scenario, IReadOnlyList<Bar> bars, int seed, int maximumClosedCuts = 24)
    {
        var random = new Random(seed);
        var full = feeder.Run(scenario, bars);
        var cuts = Cuts(full, random, maximumClosedCuts);
        var violations = new List<string>();
        var comparisons = 0;
        var contextRuns = 0;
        foreach (var cut in cuts)
        {
            var cutDate = SessionClock.DateOf(bars[cut].EpochSeconds);
            var withoutFuture = scenario.ContextDays is { } days ? scenario.WithContextDays([.. days.Where(d => d.Date < cutDate)]) : scenario;
            var withChangedFuture = scenario.ContextDays is { } all ? scenario.WithContextDays([.. all.Select(d => d.Date < cutDate ? d : OracleContext.Altered(d))]) : scenario;
            contextRuns += scenario.ContextDays is null ? 0 : 2;

            var truncated = feeder.Run(withoutFuture, [.. bars.Take(cut + 1)]);
            comparisons += Compare(scenario, full, truncated, cut, "later bars" + (scenario.ContextDays is null ? string.Empty : " and the context days from their date on") + " removed after bar " + cut, violations);
            var perturbed = feeder.Run(withChangedFuture, Perturb(bars, cut, random));
            comparisons += Compare(scenario, full, perturbed, cut, "later bars" + (scenario.ContextDays is null ? string.Empty : " and the context days from their date on") + " replaced after bar " + cut, violations);
        }

        var values = new Dictionary<Truth, int> { [Truth.True] = 0, [Truth.False] = 0, [Truth.Unknown] = 0 };
        for (var i = 0; i < bars.Count; i++)
        {
            if (full.Closed[i])
            {
                foreach (var truth in full.Truths[i])
                {
                    values[truth]++;
                }
            }
        }

        return new LookAheadReport(violations, cuts.Count, comparisons, values) { ContextRuns = contextRuns };
    }

    /// <summary>The bars up to <paramref name="cut"/> as they were, and every later bar at the same time with arbitrary values.</summary>
    public static Bar[] Perturb(IReadOnlyList<Bar> bars, int cut, Random random)
    {
        var result = new Bar[bars.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = i <= cut ? bars[i] : Junk(bars[i].EpochSeconds, random);
        }

        return result;
    }

    private static Bar Junk(long epoch, Random random)
    {
        switch (random.Next(7))
        {
            case 0:
                {
                    var p = 1e12 * (1 + random.NextDouble());
                    return new Bar(epoch, p, p * 1.5, p * 0.5, p, 10_000_000_000_000);
                }

            case 1:
                {
                    var p = 1e-9 * (1 + random.NextDouble());
                    return new Bar(epoch, p, p, p, p, 0);
                }

            case 2:
                {
                    var open = 1 + random.NextDouble() * 4_000;
                    var close = 1 + random.NextDouble() * 4_000;
                    return new Bar(epoch, open, Math.Max(open, close) * 1.2, Math.Min(open, close) * 0.8, close, 0);
                }

            case 3:
                return new Bar(epoch, 0, 0, 0, 0, 0);
            case 4:
                {
                    var p = 1e300;
                    return new Bar(epoch, p, p, p / 2, p, long.MaxValue / 4);
                }

            case 5:
                return new Bar(epoch, 5, 5, 5, 5, 0);
            default:
                {
                    var open = 1 + random.NextDouble() * 4_000;
                    var close = 1 + random.NextDouble() * 4_000;
                    return new Bar(epoch, open, Math.Max(open, close), Math.Min(open, close), close, random.Next(0, 1_000_000));
                }
        }
    }

    private static List<int> Cuts(RunLog full, Random random, int maximumClosedCuts)
    {
        var steps = full.Closed.Length;
        var closed = Enumerable.Range(0, steps).Where(i => full.Closed[i]).ToList();
        var cuts = new SortedSet<int>();
        if (closed.Count <= maximumClosedCuts)
        {
            cuts.UnionWith(closed);
        }
        else
        {
            cuts.Add(closed[0]);
            cuts.Add(closed[^1]);
            while (cuts.Count < maximumClosedCuts)
            {
                cuts.Add(closed[random.Next(closed.Count)]);
            }
        }

        for (var i = 0; i < 4 && steps > 1; i++)
        {
            cuts.Add(random.Next(0, steps));
        }

        return [.. cuts];
    }

    /// <summary>Compares the answers at the closed steps up to the cut; returns how many values were compared and adds each difference to <paramref name="violations"/>.</summary>
    private static int Compare(Scenario scenario, RunLog full, RunLog other, int cut, string what, List<string> violations)
    {
        var compared = 0;
        for (var i = 0; i <= cut; i++)
        {
            if (!full.Closed[i])
            {
                continue;
            }

            if (other.Closed[i] != full.Closed[i])
            {
                violations.Add($"{what}: step {i} was a closed bar and is not any more");
                continue;
            }

            for (var k = 0; k < scenario.Sources.Count; k++)
            {
                compared++;
                if (other.Truths[i][k] != full.Truths[i][k] || other.Decisions[i][k] != full.Decisions[i][k])
                {
                    violations.Add($"{what}: step {i}, `{scenario.Sources[k]}` was {full.Truths[i][k]} and became {other.Truths[i][k]}");
                }
            }
        }

        return compared;
    }
}
