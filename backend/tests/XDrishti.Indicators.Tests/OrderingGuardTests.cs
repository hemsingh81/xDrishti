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

    /// <summary>Every value, every history entry and the closed counts after a step.</summary>
    private static double[] Snapshot(FeatureFrame frame)
    {
        var row = new List<double>();
        for (var h = 0; h < frame.Count; h++)
        {
            for (var lag = 0; lag < 3; lag++)
            {
                row.Add(frame.At(h, lag));
            }
        }

        row.AddRange(new[] { Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.D1 }.Select(t => (double)frame.ClosedBarsAt(t)));
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
