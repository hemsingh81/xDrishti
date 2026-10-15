using System.Diagnostics;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Benchmarks;

/// <summary>
/// A small benchmark harness without BenchmarkDotNet. The tests are <b>explicit</b>: <c>dotnet test</c> and <c>scripts/check.sh</c> skip them. Run them in Release:
/// <c>dotnet test --project tests/XDrishti.Indicators.Tests -c Release -- --explicit only --filter-class "*ThroughputBenchmarks*"</c>.
/// They print updates per second per indicator (direct call and through <see cref="IIndicator"/>) and the microseconds of one step of a 12-feature <see cref="FeatureFrame"/>, and assert only generous floors
/// (the spec's targets are 10 million simple-indicator updates per second per core and 1 microsecond per 12-feature step).
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class ThroughputBenchmarks
{
    private const int Steps = 1_000_000;

    private const int Repeats = 5;

    /// <summary>Strictly time-ordered bars, enough for one pass of <see cref="Steps"/> steps after a warm-up pass.</summary>
    private static readonly Lazy<Bar[]> LazyBars = new(() => [.. SeriesGenerator.Sessions(5, 2_700)]);

    private static Bar[] Bars => LazyBars.Value;

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
    public void Timeframe_builders_bars_per_second()
    {
        var report = new List<string>();
        foreach (var timeframe in TestSpecs.HigherTimeframes)
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
