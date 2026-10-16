using System.Diagnostics;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Benchmarks;

/// <summary>
/// A small benchmark harness without BenchmarkDotNet. The tests are <b>explicit</b>: <c>dotnet test</c> and <c>scripts/check.sh</c> skip them. Run them in Release:
/// <c>dotnet test --project tests/XDrishti.Indicators.Tests -c Release -- --explicit only --filter-class "*ThroughputBenchmarks*"</c>.
/// They print updates per second per indicator (through <see cref="IIndicator"/>, the P3-01 ones and the P3-01b ones), the bars per second of every timeframe builder (the weekly and monthly ones included) and the microseconds of
/// one step of a 12-feature, of a 20-feature, of a 20-feature frame with six calendar and market-context features and of a 28-feature (1m, 1h, 1D, 1W, 1M lanes) <see cref="FeatureFrame"/> with history depth 8, and
/// assert only the floors of the specs (10 million updates per second per core for each indicator, 1 microsecond per 1-minute step of the frames).
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class ThroughputBenchmarks
{
    private const int Steps = 1_000_000;

    private const int Repeats = 5;

    /// <summary>Strictly time-ordered bars, enough for one pass of <see cref="Steps"/> steps after a warm-up pass.</summary>
    private static readonly Lazy<Bar[]> LazyBars = new(() => [.. SeriesGenerator.Sessions(5, 2_700)]);

    private static Bar[] Bars => LazyBars.Value;

    /// <summary>Consecutive weekdays from mid-2024 for 3,300 days (to 2037): every date of <see cref="Bars"/> (2026 to 2036) is inside, with a warm-up of more than 265 days before it.</summary>
    private static readonly Lazy<MarketContext> LazyContext = new(() => FrameContextFixtures.Build(FrameContextFixtures.Consecutive(88, 3_300)));

    /// <summary>Every Tuesday from 2026 to 2037, the expiry dates of the context benchmarks.</summary>
    private static readonly Lazy<IReadOnlySet<DateOnly>> LazyTuesdays = new(() => new HashSet<DateOnly>(FrameContextFixtures.Dates(new DateOnly(2026, 1, 1), new DateOnly(2037, 12, 31)).Where(d => d.DayOfWeek == DayOfWeek.Tuesday)));

    [Fact(Explicit = true)]
    public void Indicator_updates_per_second()
    {
        var report = new List<string>();
        var simple = new List<(string Name, double Rate)>();
        foreach (var (name, spec) in new[]
        {
            ("sma(20)", new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 20)),
            ("ema(20)", new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, 20)),
            ("rsi(14)", new FeatureSpec("rsi", Timeframe.M1, PriceSource.Close, 14)),
            ("atr(14)", new FeatureSpec("atr", Timeframe.M1, PriceSource.Close, 14)),
            ("highest(20)", new FeatureSpec("highest", Timeframe.M1, PriceSource.High, 20)),
            ("lowest(20)", new FeatureSpec("lowest", Timeframe.M1, PriceSource.Low, 20)),
            ("vwap", new FeatureSpec("vwap")),
            ("rel_volume(20)", new FeatureSpec("rel_volume", Timeframe.M1, PriceSource.Close, 20)),
            ("or_high(15)", new FeatureSpec("or_high", Timeframe.M1, PriceSource.Close, 15)),
            ("gap_pct", new FeatureSpec("gap_pct")),
        })
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var rate = BestRate(() => Run(info.Create(spec)));
            report.Add($"{name,-16} {rate / 1e6,8:F1} M updates/s   {1e9 / rate,6:F1} ns/update");
            simple.Add((name, rate));
        }

        Report("Indicator updates per second (through IIndicator, best of " + Repeats + ")", report);
        simple.ShouldAllBe(x => x.Rate > 10_000_000, "target: 10 million simple-indicator updates per second per core");
    }

    [Fact(Explicit = true)]
    public void New_indicator_updates_per_second()
    {
        var report = new List<string>();
        var rates = new List<(string Name, double Rate)>();
        foreach (var (name, spec) in new[]
        {
            ("adx(14)", new FeatureSpec("adx", Timeframe.M1, PriceSource.Close, 14)),
            ("plus_di(14)", new FeatureSpec("plus_di", Timeframe.M1, PriceSource.Close, 14)),
            ("minus_di(14)", new FeatureSpec("minus_di", Timeframe.M1, PriceSource.Close, 14)),
            ("macd_line(12,26,9)", new FeatureSpec("macd_line", Timeframe.M1, PriceSource.Close, 12, 26, 9)),
            ("macd_signal(12,26,9)", new FeatureSpec("macd_signal", Timeframe.M1, PriceSource.Close, 12, 26, 9)),
            ("macd_hist(12,26,9)", new FeatureSpec("macd_hist", Timeframe.M1, PriceSource.Close, 12, 26, 9)),
            ("bb_upper(20,2)", new FeatureSpec("bb_upper", Timeframe.M1, PriceSource.Close, 20, 2)),
            ("bb_middle(20,2)", new FeatureSpec("bb_middle", Timeframe.M1, PriceSource.Close, 20, 2)),
            ("bb_lower(20,2)", new FeatureSpec("bb_lower", Timeframe.M1, PriceSource.Close, 20, 2)),
            ("bb_width(20,2)", new FeatureSpec("bb_width", Timeframe.M1, PriceSource.Close, 20, 2)),
            ("supertrend(10,3)", new FeatureSpec("supertrend", Timeframe.M1, PriceSource.Close, 10, 3)),
            ("supertrend_dir(10,3)", new FeatureSpec("supertrend_dir", Timeframe.M1, PriceSource.Close, 10, 3)),
            ("roc(10)", new FeatureSpec("roc", Timeframe.M1, PriceSource.Close, 10)),
            ("inside_bar", new FeatureSpec("inside_bar")),
            ("nr(7)", new FeatureSpec("nr", Timeframe.M1, PriceSource.Close, 7)),
            ("engulfing", new FeatureSpec("engulfing")),
            ("pivot_high(5)", new FeatureSpec("pivot_high", Timeframe.M1, PriceSource.Close, 5)),
            ("pivot_low(5)", new FeatureSpec("pivot_low", Timeframe.M1, PriceSource.Close, 5)),
            ("pivot_high_age(5)", new FeatureSpec("pivot_high_age", Timeframe.M1, PriceSource.Close, 5)),
            ("pivot_low_age(5)", new FeatureSpec("pivot_low_age", Timeframe.M1, PriceSource.Close, 5)),
            ("close", new FeatureSpec("close")),
        })
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var rate = BestRate(() => Run(info.Create(spec)));
            report.Add($"{name,-22} {rate / 1e6,8:F1} M updates/s   {1e9 / rate,6:F1} ns/update");
            rates.Add((name, rate));
        }

        Report("New indicator updates per second (through IIndicator, best of " + Repeats + ")", report);
        rates.ShouldAllBe(x => x.Rate > 10_000_000, "target: 10 million updates per second per core for each new indicator");
    }

    [Fact(Explicit = true)]
    public void Timeframe_builders_bars_per_second()
    {
        var report = new List<string>();
        foreach (var timeframe in TestSpecs.DerivedTimeframes)
        {
            var rate = BestRate(() =>
            {
                var builder = new TimeframeBuilder(timeframe);
                var closed = 0;
                for (var i = 0; i < Steps; i++)
                {
                    closed += builder.Push(Bars[i], out _, out _);
                }

                return closed;
            });
            report.Add($"{timeframe.ToCode(),-16} {rate / 1e6,8:F1} M minute bars/s   {1e9 / rate,6:F1} ns/bar");
        }

        Report("TimeframeBuilder.Push (best of " + Repeats + ")", report);
    }

    [Fact(Explicit = true)]
    public void Twelve_feature_frame_step_in_microseconds()
    {
        var frame = AllocationTests.TwelveFeatureFrame();
        var rate = BestRate(() =>
        {
            frame.Reset();
            for (var i = 0; i < Steps; i++)
            {
                frame.Step(Bars[i]);
            }

            return frame.Count;
        });

        var microseconds = 1e6 / rate;
        Report("FeatureFrame, 12 features on 1m/5m/15m/1h (best of " + Repeats + ")", [$"{rate / 1e6,8:F2} M steps/s   {microseconds,6:F3} µs per 1-minute step"]);
        microseconds.ShouldBeLessThan(1.0, "target: a frame of 12 features in at most 1 microsecond per step");
    }

    [Fact(Explicit = true)]
    public void Twenty_feature_frame_with_history_step_in_microseconds()
    {
        var frame = AllocationTests.TwentyFeatureFrame();
        var rate = BestRate(() =>
        {
            frame.Reset();
            for (var i = 0; i < Steps; i++)
            {
                frame.Step(Bars[i]);
            }

            return frame.Count;
        });

        var microseconds = 1e6 / rate;
        Report("FeatureFrame, 20 features on 1m/5m/15m/1h/1D, history depth 8 (best of " + Repeats + ")", [$"{rate / 1e6,8:F2} M steps/s   {microseconds,6:F3} µs per 1-minute step"]);
        microseconds.ShouldBeLessThan(1.0, "target: a frame of 20 features in at most 1 microsecond per step");
    }

    [Fact(Explicit = true)]
    public void Twenty_feature_frame_with_six_context_features_step_in_microseconds()
    {
        var frame = AllocationTests.TwentyFeatureFrameWithContext(null, LazyContext.Value, LazyTuesdays.Value);
        var rate = BestRate(() =>
        {
            frame.Reset();
            for (var i = 0; i < Steps; i++)
            {
                frame.Step(Bars[i]);
            }

            return frame.Count;
        });

        var microseconds = 1e6 / rate;
        frame.Count.ShouldBe(26);
        frame.IsReady(frame.HandleOf(new FeatureSpec("regime"))).ShouldBeTrue("the context features had numbers in the measured window");
        Report("FeatureFrame, 20 features on 1m/5m/15m/1h/1D + 6 context features (vix, vix_pct, index_trend, regime, day_of_week, is_expiry_day), history depth 8 (best of " + Repeats + ")", [$"{rate / 1e6,8:F2} M steps/s   {microseconds,6:F3} µs per 1-minute step"]);
        microseconds.ShouldBeLessThan(1.0, "target: 20 features with 6 context features in at most 1 microsecond per step");
    }

    [Fact(Explicit = true)]
    public void Date_driven_feature_updates_per_second()
    {
        var report = new List<string>();
        var rates = new List<double>();
        foreach (var kind in Enum.GetValues<ContextFeatureKind>())
        {
            var rate = BestRate(() => Run(new ContextFeature(kind, LazyContext.Value, LazyTuesdays.Value)));
            report.Add($"{kind,-16} {rate / 1e6,8:F1} M updates/s   {1e9 / rate,6:F1} ns/update");
            rates.Add(rate);
        }

        Report("ContextFeature.Update through IIndicator, one date per 375 bars (best of " + Repeats + ")", report);
        rates.ShouldAllBe(x => x > 10_000_000, "target: 10 million updates per second per core");
    }

    [Fact(Explicit = true)]
    public void Frame_with_weekly_and_monthly_lanes_step_in_microseconds()
    {
        foreach (var withCalendar in new[] { false, true })
        {
            var frame = AllocationTests.PeriodLaneFrame(withCalendar ? new XDrishti.Domain.Market.Calendar.TradingCalendar() : null);
            var rate = BestRate(() =>
            {
                frame.Reset();
                for (var i = 0; i < Steps; i++)
                {
                    frame.Step(Bars[i]);
                }

                return frame.Count;
            });

            var microseconds = 1e6 / rate;
            Report($"FeatureFrame, 28 features on 1m/1h/1D/1W/1M, history depth 8, {(withCalendar ? "with" : "without")} a calendar (best of " + Repeats + ")", [$"{rate / 1e6,8:F2} M steps/s   {microseconds,6:F3} µs per 1-minute step"]);
            microseconds.ShouldBeLessThan(1.0, "target: a frame with weekly and monthly lanes in at most 1 microsecond per step");
        }
    }

    private static int Run(IIndicator indicator)
    {
        var ready = 0;
        for (var i = 0; i < Steps; i++)
        {
            indicator.Update(Bars[i]);
            if (indicator.IsReady)
            {
                ready++;
            }
        }

        return ready;
    }

    /// <summary>Steps per second of the fastest of <see cref="Repeats"/> passes, after one warm-up pass.</summary>
    private static double BestRate(Func<int> pass)
    {
        pass();
        var best = double.MaxValue;
        for (var r = 0; r < Repeats; r++)
        {
            var watch = Stopwatch.StartNew();
            var result = pass();
            watch.Stop();
            GC.KeepAlive(result);
            best = Math.Min(best, watch.Elapsed.TotalSeconds);
        }

        return Steps / best;
    }

    private static void Report(string title, List<string> lines)
    {
        var text = title + "\n" + string.Join("\n", lines.Select(l => "  " + l));
        TestContext.Current.TestOutputHelper?.WriteLine(text);
    }
}
