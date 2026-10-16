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

    /// <summary>Every variant on 1m up to 1D: the short series of these theories cannot fill a week, the weekly and monthly lanes have theories of their own below.</summary>
    private static readonly List<FeatureSpec> AllSpecs = [.. TestSpecs.UpToDay.SelectMany(TestSpecs.Variants)];

    private static readonly List<FeatureSpec> PeriodSpecs = [.. TestSpecs.PeriodTimeframes.SelectMany(TestSpecs.Variants)];

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

        foreach (var timeframe in TestSpecs.DerivedTimeframes)
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
            : $"BarClosed/ClosedBarsAt of {TestSpecs.UpToDay[column - AllSpecs.Count * Lags.Length].ToCode()}";

    /// <summary>Per step: the value and the history (lags 0, 1, 2 and 7) of every spec, then how many bars each timeframe closed.</summary>
    private static double[][] Trace(List<Bar> bars, int count)
    {
        var frame = FeatureFrame.Create(AllSpecs).Value;
        var trace = new double[count][];
        for (var t = 0; t < count; t++)
        {
            frame.Step(bars[t]);
            var row = new double[AllSpecs.Count * Lags.Length + TestSpecs.UpToDay.Count];
            for (var h = 0; h < AllSpecs.Count; h++)
            {
                for (var l = 0; l < Lags.Length; l++)
                {
                    row[h * Lags.Length + l] = frame.At(h, Lags[l]);
                }
            }

            for (var i = 0; i < TestSpecs.UpToDay.Count; i++)
            {
                row[AllSpecs.Count * Lags.Length + i] = frame.ClosedBarsAt(TestSpecs.UpToDay[i]);
            }

            trace[t] = row;
        }

        return trace;
    }

    public static TheoryData<int, Timeframe> LookAheadCases
    {
        get
        {
            var data = new TheoryData<int, Timeframe>();
            foreach (var seed in Enumerable.Range(1, 60))
            {
                data.Add(seed, Timeframe.W1);
                data.Add(seed, Timeframe.Mo1);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Weekly_and_monthly_outputs_for_the_first_k_bars_do_not_depend_on_the_bars_after_them(int seed)
    {
        // About 54 weeks and 12 months of bars; the calendar (the same one for both runs: a calendar derived from the cut series would be another calendar) varies with the seed
        var series = SeriesGenerator.Thin(seed, 380);
        var options = PeriodCalendars.For(series, (CalendarKnowledge)(seed % 4)).Options();
        var bars = series.Bars;
        var cut = new Random(seed * 7919).Next(1, bars.Count + 1);

        var full = PeriodTrace(bars, bars.Count, cut, options);
        var partial = PeriodTrace(bars.Take(cut).ToList(), cut, cut, options);

        var expected = full.Where(x => x.Step < cut).ToList();
        partial.Select(x => x.Step).ShouldBe(expected.Select(x => x.Step), $"seed {seed}, cut {cut}: the steps at which a week or month closed");
        for (var i = 0; i < expected.Count; i++)
        {
            for (var c = 0; c < expected[i].Row.Length; c++)
            {
                if (!Tolerance.Identical(expected[i].Row[c], partial[i].Row[c]))
                {
                    Assert.Fail($"seed {seed}, cut {cut}: {DescribePeriodColumn(c)} at step {expected[i].Step} was {partial[i].Row[c]:R} with {cut} bars and {expected[i].Row[c]:R} with {bars.Count}");
                }
            }
        }

        foreach (var timeframe in TestSpecs.PeriodTimeframes)
        {
            var wholeBuckets = BuilderRun.Run(bars, timeframe, new TimeframeBuilder(timeframe, options.Calendar)).Where(x => x.Step < cut).ToList();
            BuilderRun.Run(bars.Take(cut).ToList(), timeframe, new TimeframeBuilder(timeframe, options.Calendar)).ShouldBe(wholeBuckets, $"seed {seed}, cut {cut}, {timeframe.ToCode()}");
        }
    }

    /// <summary>
    /// The look-ahead test of the weekly and monthly lanes: change every bar of one period (prices by a factor, volumes by a constant) and the features of that timeframe must not notice at any step inside it. Without a calendar
    /// the period closes with the first bar of the next one, so they do not notice anywhere in it; with a calendar that knows every session it closes with its own last bar, which is the first step that may differ. At the
    /// step that closes it, the last close and the volume do differ: the change is real.
    /// </summary>
    [Theory]
    [MemberData(nameof(LookAheadCases))]
    public void A_weekly_or_monthly_feature_at_a_step_inside_a_period_reads_the_bars_of_earlier_periods_only(int seed, Timeframe timeframe)
    {
        var series = SeriesGenerator.Thin(seed, 380);
        var known = PeriodCalendars.For(series, seed % 2 == 0 ? CalendarKnowledge.None : CalendarKnowledge.Full);
        var starts = series.Bars.Select(b => PeriodStartOf(b, timeframe)).ToList();
        var periods = starts.Distinct().ToList();
        var target = periods[new Random(seed * 31).Next(1, periods.Count - 1)];
        var inside = Enumerable.Range(0, starts.Count).Where(i => starts[i] == target).ToList();
        var changed = series.Bars.Select((b, i) => starts[i] == target ? new Bar(b.EpochSeconds, b.Open * 1.37, b.High * 1.37, b.Low * 1.37, b.Close * 1.37, b.Volume + 12_345) : b).ToList();
        var specs = TestSpecs.Variants(timeframe).Concat([new FeatureSpec("close", timeframe), new FeatureSpec("volume", timeframe)]).ToList();
        var original = FeatureFrame.Create(specs, known.Options()).Value;
        var altered = FeatureFrame.Create(specs, known.Options()).Value;
        var closeHandle = specs.Count - 2;
        var last = inside[^1];
        var closesWithItsLastBar = known.Calendar is not null;

        for (var t = 0; t <= last + 1; t++)
        {
            original.Step(series.Bars[t]);
            altered.Step(changed[t]);
            var mayDiffer = closesWithItsLastBar ? t >= last : t > last;
            for (var h = 0; h < specs.Count && !mayDiffer; h++)
            {
                if (!Tolerance.Identical(original[h], altered[h]) || !Tolerance.Identical(original.At(h, 1), altered.At(h, 1)))
                {
                    Assert.Fail($"seed {seed}, {timeframe.ToCode()}, step {t} of {inside[0]}..{last}: {TestSpecs.Describe(specs[h])} reads the period it is in: {original[h]:R} on the real bars, {altered[h]:R} on the changed ones");
                }
            }

            if (mayDiffer && t == (closesWithItsLastBar ? last : last + 1))
            {
                Tolerance.Identical(original[closeHandle], altered[closeHandle]).ShouldBeFalse($"seed {seed}, {timeframe.ToCode()}: the period's close is in the feature the step it closes");
                original[closeHandle].ShouldBe(series.Bars[last].Close);
                altered[closeHandle].ShouldBe(changed[last].Close);
            }
        }
    }

    private static DateOnly PeriodStartOf(Bar bar, Timeframe timeframe)
    {
        var date = XDrishti.Domain.Market.Bars.SessionClock.DateOf(bar.EpochSeconds);
        return timeframe == Timeframe.W1 ? date.AddDays(-(((int)date.DayOfWeek + 6) % 7)) : new DateOnly(date.Year, date.Month, 1);
    }

    private static string DescribePeriodColumn(int column) =>
        column < PeriodSpecs.Count * Lags.Length
            ? $"{TestSpecs.Describe(PeriodSpecs[column / Lags.Length])} At(h, {Lags[column % Lags.Length]})"
            : $"ClosedBarsAt of {TestSpecs.PeriodTimeframes[column - PeriodSpecs.Count * Lags.Length].ToCode()}";

    /// <summary>
    /// The weekly and monthly outputs at the steps where either closed a bar, at every 61st step and at the last step before the cut: the value and the history (lags 0, 1, 2 and 7) of every spec, then how many bars each
    /// timeframe closed. Between closes nothing can change, so these are the steps at which a lookahead would show.
    /// </summary>
    private static List<(int Step, double[] Row)> PeriodTrace(List<Bar> bars, int count, int cut, FrameOptions options)
    {
        var frame = FeatureFrame.Create(PeriodSpecs, options).Value;
        var rows = new List<(int, double[])>();
        for (var t = 0; t < count; t++)
        {
            frame.Step(bars[t]);
            if (!(frame.BarClosed(Timeframe.W1) || frame.BarClosed(Timeframe.Mo1) || t % 61 == 0 || t == cut - 1))
            {
                continue;
            }

            var row = new double[PeriodSpecs.Count * Lags.Length + TestSpecs.PeriodTimeframes.Count];
            for (var h = 0; h < PeriodSpecs.Count; h++)
            {
                for (var l = 0; l < Lags.Length; l++)
                {
                    row[h * Lags.Length + l] = frame.At(h, Lags[l]);
                }
            }

            for (var i = 0; i < TestSpecs.PeriodTimeframes.Count; i++)
            {
                row[PeriodSpecs.Count * Lags.Length + i] = frame.ClosedBarsAt(TestSpecs.PeriodTimeframes[i]);
            }

            rows.Add((t, row));
        }

        return rows;
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
