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
            for (var h = 0; h < AllSpecs.Count; h++)
            {
                if (!Tolerance.Identical(full[t][h], partial[t][h]))
                {
                    Assert.Fail($"seed {seed}, cut {cut}: {TestSpecs.Describe(AllSpecs[h])} at step {t} was {partial[t][h]:R} with {cut} bars and {full[t][h]:R} with {bars.Count}");
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

    /// <summary>The value of every spec after each of the first <paramref name="count"/> steps.</summary>
    private static double[][] Trace(List<Bar> bars, int count)
    {
        var frame = FeatureFrame.Create(AllSpecs).Value;
        var trace = new double[count][];
        for (var t = 0; t < count; t++)
        {
            frame.Step(bars[t]);
            var row = new double[AllSpecs.Count];
            for (var h = 0; h < row.Length; h++)
            {
                row[h] = frame[h];
            }

            trace[t] = row;
        }

        return trace;
    }
}
