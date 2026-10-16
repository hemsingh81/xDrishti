using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>A 1-minute bar at or before the previous one is counted and skipped by every lane (criterion 4 of P3-01b): the output equals the output of the clean series.</summary>
public sealed class OrderingGuardTests
{
    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 20)];

    private static List<FeatureSpec> Specs() =>
    [
        new("sma", Timeframe.M1, PriceSource.Close, 5),
        new("ema", Timeframe.M1, PriceSource.Close, 9),
        new("rsi", Timeframe.M1, PriceSource.Close, 14),
        new("vwap"),
        new("sma", Timeframe.M5, PriceSource.Close, 3),
        new("atr", Timeframe.M5, PriceSource.Close, 4),
        new("adx", Timeframe.M5, PriceSource.Close, 3),
        new("sma", Timeframe.D1, PriceSource.Close, 2),
        new("close", Timeframe.D1),
        new("volume", Timeframe.M15),
        new("pivot_high", Timeframe.M5, PriceSource.Close, 2),
        new("gap_pct"),
    ];

    private static readonly Timeframe[] SnapshotFrames = [Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.D1];

    private static readonly Timeframe[] PeriodSnapshotFrames = [Timeframe.M1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1];

    /// <summary>A frame with the 1m, 1D, 1W and 1M lanes together.</summary>
    private static List<FeatureSpec> PeriodSpecs() =>
    [
        new("sma", Timeframe.M1, PriceSource.Close, 5),
        new("close", Timeframe.D1),
        new("sma", Timeframe.D1, PriceSource.Close, 2),
        new("close", Timeframe.W1),
        new("open", Timeframe.W1),
        new("volume", Timeframe.W1),
        new("sma", Timeframe.W1, PriceSource.Close, 3),
        new("ema", Timeframe.W1, PriceSource.Close, 4),
        new("rsi", Timeframe.W1, PriceSource.Close, 3),
        new("atr", Timeframe.W1, PriceSource.Close, 2),
        new("adx", Timeframe.W1, PriceSource.Close, 2),
        new("inside_bar", Timeframe.W1),
        new("close", Timeframe.Mo1),
        new("high", Timeframe.Mo1),
        new("sma", Timeframe.Mo1, PriceSource.Close, 2),
        new("rsi", Timeframe.Mo1, PriceSource.Close, 2),
    ];

    /// <summary>Every value, every history entry and the closed counts after a step.</summary>
    private static double[] Snapshot(FeatureFrame frame) => Snapshot(frame, SnapshotFrames);

    private static double[] Snapshot(FeatureFrame frame, Timeframe[] timeframes)
    {
        var row = new List<double>();
        for (var h = 0; h < frame.Count; h++)
        {
            for (var lag = 0; lag < 3; lag++)
            {
                row.Add(frame.At(h, lag));
            }
        }

        row.AddRange(timeframes.Select(t => (double)frame.ClosedBarsAt(t)));
        row.Add(frame.Steps);
        row.Add(frame.IncompleteBarsConsumed);
        return [.. row];
    }

    private static void ShouldBeSame(double[] expected, double[] actual, string label)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (!Tolerance.Identical(expected[i], actual[i]))
            {
                Assert.Fail($"{label}: column {i} is {actual[i]:R}, the clean run has {expected[i]:R}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Repeated_and_older_bars_give_exactly_the_output_of_the_clean_series(int seed)
    {
        var clean = SeriesGenerator.Random(seed, 400, 1_500, specialSessions: true);
        var random = new Random(seed * 13);
        var dirty = new List<(Bar Bar, int CleanIndex)>();
        var repeats = 0;
        for (var i = 0; i < clean.Count; i++)
        {
            dirty.Add((clean[i], i));
            if (random.NextDouble() < 0.08)
            {
                // the same minute again (different prices, so a leak would show), or an older bar replayed
                var older = random.NextDouble() < 0.5 ? clean[i] : clean[random.Next(0, i + 1)];
                dirty.Add((older with { Close = older.Close + 7, High = older.High + 9, Volume = older.Volume + 1_000 }, -1));
                repeats++;
            }
        }

        var cleanFrame = FeatureFrame.Create(Specs()).Value;
        var dirtyFrame = FeatureFrame.Create(Specs()).Value;
        var cleanRows = new List<double[]>();
        foreach (var bar in clean)
        {
            cleanFrame.Step(bar);
            cleanRows.Add(Snapshot(cleanFrame));
        }

        foreach (var (bar, cleanIndex) in dirty)
        {
            dirtyFrame.Step(bar);
            if (cleanIndex >= 0)
            {
                ShouldBeSame(cleanRows[cleanIndex], Snapshot(dirtyFrame), $"seed {seed}, step {cleanIndex}");
            }
            else
            {
                dirtyFrame.BarClosed(Timeframe.M1).ShouldBeFalse();
                dirtyFrame.BarClosed(Timeframe.M5).ShouldBeFalse();
                dirtyFrame.BarClosed(Timeframe.D1).ShouldBeFalse();
            }
        }

        dirtyFrame.IgnoredBars.ShouldBe(repeats);
        dirtyFrame.Steps.ShouldBe(clean.Count);
        cleanFrame.IgnoredBars.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Repeated_and_older_bars_give_exactly_the_output_of_the_clean_series_with_weekly_and_monthly_lanes_too(int seed)
    {
        var series = SeriesGenerator.Thin(seed, 200);
        var clean = series.Bars;
        var options = PeriodCalendars.For(series, seed % 2 == 0 ? CalendarKnowledge.None : CalendarKnowledge.Full).Options();
        var random = new Random(seed * 13);
        var dirty = new List<(Bar Bar, int CleanIndex)>();
        var repeats = 0;
        for (var i = 0; i < clean.Count; i++)
        {
            dirty.Add((clean[i], i));
            if (random.NextDouble() < 0.02)
            {
                // the same minute again, or a bar of an earlier week or month replayed, with other prices so that a leak would show
                var older = random.NextDouble() < 0.5 ? clean[i] : clean[random.Next(0, i + 1)];
                dirty.Add((older with { Close = older.Close + 7, High = older.High + 9, Volume = older.Volume + 1_000 }, -1));
                repeats++;
            }
        }

        var cleanFrame = FeatureFrame.Create(PeriodSpecs(), options).Value;
        var dirtyFrame = FeatureFrame.Create(PeriodSpecs(), options).Value;
        var cleanRows = new List<double[]>();
        int weeks = 0, months = 0;
        foreach (var bar in clean)
        {
            cleanFrame.Step(bar);
            cleanRows.Add(Snapshot(cleanFrame, PeriodSnapshotFrames));
            weeks += cleanFrame.ClosedBarsAt(Timeframe.W1);
            months += cleanFrame.ClosedBarsAt(Timeframe.Mo1);
        }

        foreach (var (bar, cleanIndex) in dirty)
        {
            dirtyFrame.Step(bar);
            if (cleanIndex >= 0)
            {
                ShouldBeSame(cleanRows[cleanIndex], Snapshot(dirtyFrame, PeriodSnapshotFrames), $"seed {seed}, step {cleanIndex}");
            }
            else
            {
                foreach (var timeframe in PeriodSnapshotFrames)
                {
                    dirtyFrame.BarClosed(timeframe).ShouldBeFalse(timeframe.ToCode());
                }
            }
        }

        dirtyFrame.IgnoredBars.ShouldBe(repeats);
        dirtyFrame.Steps.ShouldBe(clean.Count);
        cleanFrame.IgnoredBars.ShouldBe(0);
        weeks.ShouldBeGreaterThan(20, "weeks closed in the clean run");
        months.ShouldBeGreaterThan(3, "months closed in the clean run");
    }

    [Fact]
    public void A_bar_replayed_from_an_earlier_week_is_skipped_and_neither_reopens_that_week_nor_touches_the_weekly_lane()
    {
        var series = BarFixtures.GetPeriods("holiday friday");
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.W1), new FeatureSpec("volume", Timeframe.W1), new FeatureSpec("close", Timeframe.M1)], PeriodCalendars.For(series, CalendarKnowledge.Full).Options()).Value;
        for (var t = 0; t < 1500; t++)
        {
            frame.Step(series.Bars[t]);
        }

        frame.TryLastClosed(Timeframe.W1, out var week).ShouldBeTrue();
        week.Bars.ShouldBe(1500);

        // Monday of the first week again, with a huge volume and another close: nothing may change
        frame.Step(series.Bars[10] with { Close = 999, High = 1_000, Volume = 5_000_000 });

        frame.IgnoredBars.ShouldBe(1);
        frame.Steps.ShouldBe(1500);
        frame.BarClosed(Timeframe.W1).ShouldBeFalse();
        frame.ClosedBarsAt(Timeframe.W1).ShouldBe(0);
        frame.TryLastClosed(Timeframe.W1, out var still).ShouldBeTrue();
        still.ShouldBe(week);
        frame[0].ShouldBe(series.Bars[1499].Close);
        frame[1].ShouldBe(week.Bar.Volume);
        frame[2].ShouldBe(series.Bars[1499].Close, "the 1-minute lane is on the last accepted bar");

        for (var t = 1500; t <= 3374; t++)
        {
            frame.Step(series.Bars[t]);
        }

        frame.IgnoredBars.ShouldBe(1);
        frame.TryLastClosed(Timeframe.W1, out var second).ShouldBeTrue();
        (second.Bars, second.Bar.Volume).ShouldBe((1875, series.Bars.Skip(1500).Take(1875).Sum(b => b.Volume)));
        frame.At(0, 1).ShouldBe(series.Bars[1499].Close);
    }

    [Fact]
    public void A_duplicate_is_not_fed_to_the_one_minute_lane_nor_to_the_builders()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 2), new FeatureSpec("close", Timeframe.M5), new FeatureSpec("volume", Timeframe.D1)]).Value;
        var bars = Minutes.Closes(BarFixtures.Mon, 10, 20, 30);
        foreach (var bar in bars)
        {
            frame.Step(bar);
        }

        // the last bar again with other prices and a huge volume: nothing may change
        frame.Step(bars[2] with { Close = 999, Volume = 1_000_000 });

        frame[0].ShouldBe(25.0);
        frame.At(0, 1).ShouldBe(15.0);
        frame.IgnoredBars.ShouldBe(1);
        frame.Steps.ShouldBe(3);
    }

    [Fact]
    public void An_older_bar_after_a_session_boundary_is_skipped_too()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.D1), new FeatureSpec("close", Timeframe.M1)]).Value;
        foreach (var bar in BarFixtures.Walk(BarFixtures.Mon, Minutes.Open, 375))
        {
            frame.Step(bar);
        }

        frame.Step(BarFixtures.Walk(BarFixtures.Tue, Minutes.Open, 1)[0]);
        var mondayClose = frame[0];

        frame.Step(BarFixtures.Walk(BarFixtures.Mon, Minutes.At(15, 0), 1)[0]);

        frame.IgnoredBars.ShouldBe(1);
        frame[0].ShouldBe(mondayClose);
        frame[1].ShouldBe(BarFixtures.Walk(BarFixtures.Tue, Minutes.Open, 1)[0].Close);
    }

    [Fact]
    public void Reset_clears_the_guard_and_the_counter()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("close")]).Value;
        var bars = Minutes.Closes(BarFixtures.Mon, 1, 2);
        frame.Step(bars[1]);
        frame.Step(bars[0]);
        frame.IgnoredBars.ShouldBe(1);

        frame.Reset();
        frame.Step(bars[0]);

        frame.IgnoredBars.ShouldBe(0);
        frame.Steps.ShouldBe(1);
        frame[0].ShouldBe(1.0);
    }
}
