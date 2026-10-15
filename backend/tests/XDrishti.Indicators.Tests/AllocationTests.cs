using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// After a warm-up the steady-state <c>Update</c> of every indicator and <c>FeatureFrame.Step</c> allocate nothing (criterion 13): the bytes allocated by this thread over 100,000 steps are exactly 0. The bars are
/// built before the measurement and the warm-up (20,000 steps) lets the JIT compile the paths first; the tests are synchronous so the thread counter covers all the work. They hold in Debug and Release.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class AllocationTests
{
    private const int WarmUpSteps = 20_000;

    private const int MeasuredSteps = 100_000;

    /// <summary>Gap-free, time-ordered bars for warm-up and measurement: 330 sessions of 375 minutes.</summary>
    private static readonly Bar[] Bars = [.. SeriesGenerator.Sessions(99, 330)];

    [Fact]
    public void The_series_is_long_enough_and_ordered()
    {
        Bars.Length.ShouldBeGreaterThanOrEqualTo(WarmUpSteps + MeasuredSteps);
        for (var i = 1; i < Bars.Length; i++)
        {
            Bars[i].EpochSeconds.ShouldBeGreaterThan(Bars[i - 1].EpochSeconds);
        }
    }

    [Fact]
    public void The_measurement_notices_an_allocation_when_there_is_one()
    {
        object? keep = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            keep = new byte[16];
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeGreaterThanOrEqualTo(16_000);
        GC.KeepAlive(keep);
    }

    [Fact]
    public void Update_allocates_nothing_for_every_indicator()
    {
        foreach (var spec in IndicatorCatalog.SupportedTimeframes.SelectMany(TestSpecs.Variants).Where(s => s.Timeframe == Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var indicator = info.Create(spec);
            for (var i = 0; i < WarmUpSteps; i++)
            {
                indicator.Update(Bars[i]);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                indicator.Update(Bars[i]);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            allocated.ShouldBe(0, TestSpecs.Describe(spec));
        }
    }

    [Fact]
    public void Update_and_reading_the_value_allocate_nothing_through_the_interface()
    {
        IIndicator[] indicators = [new Sma(20), new Ema(20), new Rsi(14), new Atr(14), new Highest(20), new Lowest(20), new Vwap(), new RelVolume(20), new OrHigh(15), new OrLow(15), new SessionContext(SessionField.GapPct)];
        foreach (var indicator in indicators)
        {
            for (var i = 0; i < WarmUpSteps; i++)
            {
                indicator.Update(Bars[i]);
            }
        }

        var sink = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
        {
            foreach (var indicator in indicators)
            {
                indicator.Update(Bars[i]);
                sink += indicator.IsReady ? indicator.Value : 0;
            }
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
        double.IsNaN(sink).ShouldBeFalse();
    }

    [Fact]
    public void The_timeframe_builders_allocate_nothing()
    {
        foreach (var timeframe in TestSpecs.HigherTimeframes)
        {
            var builder = new TimeframeBuilder(timeframe);
            for (var i = 0; i < WarmUpSteps; i++)
            {
                builder.Push(Bars[i], out _, out _);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                builder.Push(Bars[i], out _, out _);
            }

            (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0, timeframe.ToCode());
        }
    }

    [Fact]
    public void A_builder_that_consults_a_calendar_allocates_nothing()
    {
        var calendar = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 3, 4), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);
        var builder = new TimeframeBuilder(Timeframe.M15, calendar);
        for (var i = 0; i < WarmUpSteps; i++)
        {
            builder.Push(Bars[i], out _, out _);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
        {
            builder.Push(Bars[i], out _, out _);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    [Fact]
    public void A_twelve_feature_frame_step_allocates_nothing()
    {
        var frame = TwelveFeatureFrame();
        for (var i = 0; i < WarmUpSteps; i++)
        {
            frame.Step(Bars[i]);
        }

        var sink = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
        {
            frame.Step(Bars[i]);
            for (var h = 0; h < frame.Count; h++)
            {
                sink += frame.IsReady(h) ? frame[h] : 0;
            }
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
        double.IsNaN(sink).ShouldBeFalse();
    }

    /// <summary>Twelve features over four timeframes, the mix a strategy would use.</summary>
    internal static FeatureFrame TwelveFeatureFrame() => FeatureFrame.Create(
    [
        new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 20),
        new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, 50),
        new FeatureSpec("rsi", Timeframe.M1, PriceSource.Close, 14),
        new FeatureSpec("atr", Timeframe.M1, PriceSource.Close, 14),
        new FeatureSpec("vwap", Timeframe.M1),
        new FeatureSpec("rel_volume", Timeframe.M1, PriceSource.Close, 20),
        new FeatureSpec("ema", Timeframe.M5, PriceSource.Close, 20),
        new FeatureSpec("rsi", Timeframe.M15, PriceSource.Close, 14),
        new FeatureSpec("highest", Timeframe.M15, PriceSource.High, 20),
        new FeatureSpec("atr", Timeframe.H1, PriceSource.Close, 14),
        new FeatureSpec("or_high", Timeframe.M1, PriceSource.Close, 15),
        new FeatureSpec("gap_pct", Timeframe.M1),
    ]).Value;
}
