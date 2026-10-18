using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>
/// Criterion 26: the truncation and future-perturbation property of P3-02 (criterion 10) run through <c>BoundSide.Evaluate</c> and <c>InEntryWindow</c>. For random sides (two or three items per list) on 5m, 15m, 1h and 1D, with random
/// time rules on the intraday ones, the three truths, <c>Fires</c> and the window at every closed bar of the evaluation timeframe are the same when all later bars are removed and when they are replaced by arbitrary values;
/// <c>NextBarFeeder</c>'s counterpart, the negative control, is reported as a violation. This guards the feed through the new layer; the exact meaning stays with the oracle and the hand examples.
/// </summary>
public sealed class SideLookAheadTests
{
    private static readonly Timeframe[] Evaluations = [Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1];

    private readonly record struct Step(bool Closed, SideEvaluation Evaluation, bool Window, string Items);

    /// <summary>How a series is fed to the frame of a side and read; the honest one reads after the bar of the step, the control reads after the next bar.</summary>
    private interface ISideFeeder
    {
        Step[] Run(CompiledSide side, IReadOnlyList<Bar> bars);
    }

    private sealed class Honest : ISideFeeder
    {
        public Step[] Run(CompiledSide side, IReadOnlyList<Bar> bars)
        {
            var rig = SideRig.Of(side);
            var log = new Step[bars.Count];
            for (var i = 0; i < bars.Count; i++)
            {
                rig.Frame.Step(bars[i]);
                log[i] = Read(rig, side.EvaluationTimeframe);
            }

            return log;
        }
    }

    private sealed class NextBar : ISideFeeder
    {
        public Step[] Run(CompiledSide side, IReadOnlyList<Bar> bars)
        {
            var rig = SideRig.Of(side);
            var log = new Step[bars.Count];
            for (var i = 0; i < bars.Count; i++)
            {
                rig.Frame.Step(bars[i]);
                if (i > 0)
                {
                    var closed = log[i - 1].Closed;
                    log[i - 1] = Read(rig, side.EvaluationTimeframe) with { Closed = closed };
                }

                log[i] = new Step(rig.Frame.BarClosed(side.EvaluationTimeframe), default, false, string.Empty);
            }

            if (bars.Count > 0)
            {
                log[^1] = Read(rig, side.EvaluationTimeframe);
            }

            return log;
        }
    }

    private static Step Read(SideRig rig, Timeframe evaluation)
    {
        var evaluated = rig.Evaluate();
        return new Step(rig.Frame.BarClosed(evaluation), evaluated, rig.Bound.InEntryWindow(rig.Frame), rig.Word);
    }

    /// <summary>A side of random conditions: two or three items per list, random modes, random time rules on the intraday timeframes.</summary>
    private static (CompiledSide Side, Bar[] Bars) SideFor(int seed, Timeframe evaluation)
    {
        var random = new Random(seed);
        var generator = new ModelGenerator(seed, evaluation, false, false);
        ConditionList List() => new(random.Next(2) == 0 ? ListMode.All : ListMode.Any, [.. Enumerable.Range(0, random.Next(2, 4)).Select(_ => Printer.Print(generator.Next()))]);
        var parameters = ModelGenerator.Parameters.Select(p => new StrategyParam(p.Key, p.Value)).ToList();
        var swing = evaluation == Timeframe.D1;
        var first = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes((9 * 60) + 15 + (random.Next(0, 4) * 15)));
        var rules = swing
            ? new TimeRules(null, null, null, 3)
            : new TimeRules(random.Next(2) == 0 ? first : null, random.Next(2) == 0 ? TimeOnly.FromTimeSpan(TimeSpan.FromMinutes((11 * 60) + 30 + (random.Next(0, 3) * 15))) : null, new TimeOnly(15, 15), null);
        var definition = Defs.Basic().WithBuy(b => b with { Setup = random.Next(3) == 0 ? null : List(), Trigger = List(), Invalidate = random.Next(2) == 0 ? null : List() }) with
        {
            Style = swing ? Style.Swing : Style.Intraday,
            Timeframes = [evaluation],
            Sides = [XDrishti.Domain.Research.Side.Buy],
            Sell = null,
            Params = parameters,
            TimeRules = rules,
        };
        return (Located.Ok(definition, evaluation), OracleSeries.For(seed, evaluation, light: true));
    }

    private static List<int> Cuts(Step[] full, Random random)
    {
        var closed = Enumerable.Range(0, full.Length).Where(i => full[i].Closed).ToList();
        var cuts = new SortedSet<int>();
        if (closed.Count > 0)
        {
            cuts.Add(closed[0]);
            cuts.Add(closed[^1]);
            for (var i = 0; i < 4; i++)
            {
                cuts.Add(closed[random.Next(closed.Count)]);
            }
        }

        cuts.Add(random.Next(0, full.Length));
        return [.. cuts];
    }

    /// <summary>The differences found at the closed steps up to the cut, and how many values were compared.</summary>
    private static int Compare(Step[] full, Step[] other, int cut, string what, List<string> violations)
    {
        var compared = 0;
        for (var i = 0; i <= cut; i++)
        {
            if (!full[i].Closed)
            {
                continue;
            }

            compared++;
            if (other[i].Closed != full[i].Closed || other[i].Evaluation != full[i].Evaluation || other[i].Window != full[i].Window || other[i].Items != full[i].Items)
            {
                violations.Add($"{what}: step {i} was {full[i].Evaluation} window {full[i].Window} and became {other[i].Evaluation} window {other[i].Window}");
            }
        }

        return compared;
    }

    private static (List<string> Violations, int Comparisons, int Fires, int Unknowns, int Windows) Check(ISideFeeder feeder, int seeds, int firstSeed)
    {
        var violations = new List<string>();
        var comparisons = 0;
        var fires = 0;
        var unknowns = 0;
        var windows = 0;
        for (var s = 0; s < seeds; s++)
        {
            var seed = firstSeed + s;
            var evaluation = Evaluations[s % Evaluations.Length];
            var (side, bars) = SideFor(seed, evaluation);
            var random = new Random(seed);
            var full = feeder.Run(side, bars);
            foreach (var cut in Cuts(full, random))
            {
                var truncated = feeder.Run(side, [.. bars.Take(cut + 1)]);
                comparisons += Compare(full, truncated, cut, $"seed {seed} {evaluation}: later bars removed after bar {cut}", violations);
                var perturbed = feeder.Run(side, LookAheadProperty.Perturb(bars, cut, random));
                comparisons += Compare(full, perturbed, cut, $"seed {seed} {evaluation}: later bars replaced after bar {cut}", violations);
            }

            fires += full.Count(f => f.Closed && f.Evaluation.Fires);
            unknowns += full.Count(f => f.Closed && (f.Evaluation.Setup == Truth.Unknown || f.Evaluation.Trigger == Truth.Unknown || f.Evaluation.Invalidate == Truth.Unknown));
            windows += full.Count(f => f.Closed && f.Window);
        }

        return (violations, comparisons, fires, unknowns, windows);
    }

    [Fact]
    public void Removing_or_replacing_every_later_bar_changes_no_truth_no_decision_and_no_window_at_a_closed_bar_for_60_sides()
    {
        var (violations, comparisons, _, unknowns, windows) = Check(new Honest(), 60, 41_000);

        violations.ShouldBeEmpty(violations.Count + " answers changed when later bars changed:\n" + string.Join("\n", violations.Take(8)));
        comparisons.ShouldBeGreaterThan(60 * 6 * 2 * 3, "too few values compared");
        unknowns.ShouldBeGreaterThan(0, "the property is not only about known values");
        windows.ShouldBeGreaterThan(0, "the window is open at some closed bars");
    }

    [Fact]
    public void The_negative_control_that_reads_the_first_bar_after_the_step_is_reported_as_a_violation()
    {
        var (violations, _, _, _, _) = Check(new NextBar(), 24, 41_000);

        violations.ShouldNotBeEmpty("the feeder that leaks the next bar must be caught, or the property proves nothing");
    }
}
