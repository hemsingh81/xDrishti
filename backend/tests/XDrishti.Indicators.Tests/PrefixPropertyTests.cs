using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// The core guarantee (criterion 8): an output for steps up to <i>k</i> does not change when more bars are fed afterwards. Feeding only the first <i>k</i> bars gives, bit for bit, the outputs the full series
/// gives for those steps — for every indicator, the timeframe builders and the frame.
/// </summary>
public sealed class PrefixPropertyTests
{
    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 220)];

    private static readonly List<FeatureSpec> AllSpecs = [.. IndicatorCatalog.SupportedTimeframes.SelectMany(TestSpecs.Variants)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Outputs_for_the_first_k_bars_do_not_depend_on_the_bars_after_them(int seed)
    {
        var bars = SeriesGenerator.Random(seed, 300, 1_500, specialSessions: true);
        var cut = new Random(seed * 7919).Next(1, bars.Count + 1);
        var prefix = bars.Take(cut).ToList();

        var full = Trace(bars, bars.Count);
        var partial = Trace(prefix, cut);

        for (var t = 0; t < cut; t++)
        {
            for (var c = 0; c < full[t].Length; c++)
            {
                if (!Tolerance.Identical(full[t][c], partial[t][c]))
                {
                    Assert.Fail($"seed {seed}, cut {cut}: {Describe(c)} at step {t} was {partial[t][c]:R} with {cut} bars and {full[t][c]:R} with {bars.Count}");
                }
            }
        }

        foreach (var timeframe in TestSpecs.HigherTimeframes)
        {
            var wholeBuckets = BuilderRun.Run(bars, timeframe).Where(x => x.Step < cut).ToList();
            BuilderRun.Run(prefix, timeframe).ShouldBe(wholeBuckets, $"seed {seed}, cut {cut}, {timeframe.ToCode()}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Indicators_fed_directly_obey_the_same_property(int seed)
    {
        var bars = SeriesGenerator.Random(seed, 300, 1_200);
        var cut = new Random(seed * 31 + 5).Next(1, bars.Count + 1);

        foreach (var spec in TestSpecs.Variants(Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var whole = info.Create(spec);
            var partial = info.Create(spec);
            for (var t = 0; t < bars.Count; t++)
            {
                whole.Update(bars[t]);
                if (t < cut)
                {
                    partial.Update(bars[t]);
                    if (!Tolerance.Identical(whole.Value, partial.Value) || whole.IsReady != partial.IsReady)
                    {
                        Assert.Fail($"seed {seed}, cut {cut}: {TestSpecs.Describe(spec)} differs at step {t}");
                    }
                }
            }
        }
    }

    private static readonly int[] Lags = [0, 1, 2, 7];

    private static string Describe(int column) =>
        column < AllSpecs.Count * Lags.Length
            ? $"{TestSpecs.Describe(AllSpecs[column / Lags.Length])} At(h, {Lags[column % Lags.Length]})"
            : $"BarClosed/ClosedBarsAt of {IndicatorCatalog.SupportedTimeframes[column - AllSpecs.Count * Lags.Length].ToCode()}";

    /// <summary>Per step: the value and the history (lags 0, 1, 2 and 7) of every spec, then how many bars each timeframe closed.</summary>
    private static double[][] Trace(List<Bar> bars, int count)
    {
        var frame = FeatureFrame.Create(AllSpecs).Value;
        var trace = new double[count][];
        for (var t = 0; t < count; t++)
        {
            frame.Step(bars[t]);
            var row = new double[AllSpecs.Count * Lags.Length + IndicatorCatalog.SupportedTimeframes.Count];
            for (var h = 0; h < AllSpecs.Count; h++)
            {
                for (var l = 0; l < Lags.Length; l++)
                {
                    row[h * Lags.Length + l] = frame.At(h, Lags[l]);
                }
            }

            for (var i = 0; i < IndicatorCatalog.SupportedTimeframes.Count; i++)
            {
                row[AllSpecs.Count * Lags.Length + i] = frame.ClosedBarsAt(IndicatorCatalog.SupportedTimeframes[i]);
            }

            trace[t] = row;
        }

        return trace;
    }

    [Fact]
    public void Pivots_confirmed_at_a_step_do_not_depend_on_the_bars_after_it_on_a_zigzag_series()
    {
        // a zigzag makes a pivot almost every few bars; feeding every prefix gives the same values as the full run at that step
        var day = new DateOnly(2026, 3, 2);
        var random = new Random(77);
        var bars = Enumerable.Range(0, 300).Select(i => Minutes.Bar(day, Minutes.Open + i, 100, 100 + random.Next(0, 6), 94 - random.Next(0, 6), 100)).ToList();
        FeatureSpec[] specs = [new("pivot_high", Timeframe.M1, PriceSource.Close, 2), new("pivot_low", Timeframe.M1, PriceSource.Close, 3), new("pivot_high_age", Timeframe.M1, PriceSource.Close, 1), new("pivot_low_age", Timeframe.M1, PriceSource.Close, 2)];
        var full = FeatureFrame.Create(specs).Value;
        var fullRows = new List<double[]>();
        foreach (var bar in bars)
        {
            full.Step(bar);
            fullRows.Add([full[0], full[1], full[2], full[3]]);
        }

        for (var cut = 1; cut <= bars.Count; cut += 7)
        {
            var frame = FeatureFrame.Create(specs).Value;
            for (var t = 0; t < cut; t++)
            {
                frame.Step(bars[t]);
            }

            for (var h = 0; h < specs.Length; h++)
            {
                Tolerance.Identical(frame[h], fullRows[cut - 1][h]).ShouldBeTrue($"{specs[h].Name} after {cut} bars");
            }
        }

        fullRows.Count(r => !double.IsNaN(r[0])).ShouldBeGreaterThan(100, "the series really produces pivots");
    }
}
