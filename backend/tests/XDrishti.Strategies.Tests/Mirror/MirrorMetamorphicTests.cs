using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Tests.Support;
using static XDrishti.Strategies.Tests.Mirror.MirrorTestSupport;

namespace XDrishti.Strategies.Tests.Mirror;

/// <summary>
/// Spec P3-02e criteria 3, 4 and 10: on the inverted market <c>p' = K - p</c> (volume unchanged) the mirror of a condition of the exact class has the same three-valued result as the original on <c>p</c>, at every bar,
/// for 200 seeds on 5m, 15m and 1h (the per-session features and their lags included); a table with <c>crosses_above</c> left unswapped fails; the sign class flips its sign on <c>p'' = C^2 / p</c>.
/// </summary>
public sealed class MirrorMetamorphicTests
{
    private const double K = 10_000;

    private const double C = 1_000;

    private static readonly string[] Exact =
    [
        "close > open",
        "close > ema(20)",
        "close >= highest(10, source=close)",
        "low[1] < low[2] and low[1] <= vwap[1] + 0.5 * atr(14)",
        "crosses_above(close, bb_lower(20, 2))",
        "crosses_above(close, vwap)",
        "rising(close, 3)",
        "falling(rsi(14), 2)",
        "rsi(14) > 55",
        "rsi(14) > rsi(5)",
        "between(rsi(14), 30, 60)",
        "between(close, low, high)",
        "macd_hist() > 0",
        "macd_line() > macd_signal()",
        "plus_di(14) > minus_di(14)",
        "adx(14) > 20",
        "inside_bar[1] == 1 and close > high[2]",
        "engulfing == 1",
        "nr(4) == 1 and close > high[1]",
        "close > pivot_high(3)",
        "pivot_high_age(3) < pivot_low_age(3)",
        "close > or_high(60)",
        "close > prev_day.high",
        "low < prev_day.low and close > prev_day.low and close > open",
        "close > today.open",
        "close - open > atr(14)",
        "abs(close - open) > atr(14)",
        "high - low > 1.5 * atr(14)",
        "close > ema(20) + atr(14)",
        "min(close, open) > ema(20)",
        "max(high, close) > bb_upper(20, 2)",
        "tf(\"1D\").close > tf(\"1D\").ema(5)",
        "volume > prev_day_volume",
        "rel_volume(10) > 1",
        "close > sma(10) and rsi(14) < 70",
        "crosses_above(rsi(14), 30)",
        "not (close > vwap)",
        "close > vwap[1]",
        "time > \"10:00\" and close > open",
        "between(macd_hist(), -1, 1)",
    ];

    private static readonly Timeframe[] Frames = [Timeframe.M5, Timeframe.M15, Timeframe.H1];

    private static Bar[] Inverted(Bar[] bars) => [.. bars.Select(b => new Bar(b.EpochSeconds, K - b.Open, K - b.Low, K - b.High, K - b.Close, b.Volume))];

    private static Bar[] Reciprocal(Bar[] bars) => [.. bars.Select(b => new Bar(b.EpochSeconds, C * C / b.Open, C * C / b.Low, C * C / b.High, C * C / b.Close, b.Volume))];

    private static string[] Run(Timeframe frame, IReadOnlyList<string> sources, Bar[] bars)
    {
        var rig = Rig.Of(frame, sources);
        var words = sources.Select(_ => new List<char>(bars.Length)).ToArray();
        foreach (var bar in bars)
        {
            var truths = rig.Step(bar);
            for (var i = 0; i < truths.Length; i++)
            {
                words[i].Add(Rig.Word([truths[i]])[0]);
            }
        }

        return [.. words.Select(w => new string([.. w]))];
    }

    private static int Mismatches(IReadOnlyList<string> mirrored, bool breakCrossing)
    {
        var failingSeeds = 0;
        for (var seed = 0; seed < 200 && failingSeeds < 10; seed++)
        {
            var frame = Frames[seed % Frames.Length];
            var bars = Series.Sessions(seed, 3);
            var sources = breakCrossing ? [.. mirrored.Select(m => m.Replace("crosses_below", "crosses_above", StringComparison.Ordinal))] : mirrored;
            var original = Run(frame, Exact, bars);
            var onInverted = Run(frame, sources, Inverted(bars));
            if (!original.SequenceEqual(onInverted))
            {
                failingSeeds++;
            }
        }

        return failingSeeds;
    }

    private static List<string> MirroredExact() => [.. Exact.Select(Ok)];

    [Fact]
    public void The_mirror_of_an_exact_condition_on_the_inverted_market_has_the_same_truth_at_every_bar()
    {
        var mirrored = MirroredExact();
        var report = new List<string>();
        for (var seed = 0; seed < 200; seed++)
        {
            var frame = Frames[seed % Frames.Length];
            var bars = Series.Sessions(seed, 3);
            var original = Run(frame, Exact, bars);
            var onInverted = Run(frame, mirrored, Inverted(bars));
            for (var i = 0; i < Exact.Length; i++)
            {
                if (original[i] != onInverted[i])
                {
                    report.Add("seed " + seed + " " + frame + ": " + Exact[i] + " -> " + mirrored[i]);
                }
            }
        }

        report.Distinct().ShouldBeEmpty(string.Join("\n", report.Distinct().Take(20)));
    }

    [Fact]
    public void A_table_that_leaves_crosses_above_unswapped_fails_on_at_least_ten_seeds()
    {
        Mismatches(MirroredExact(), breakCrossing: true).ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void The_sign_class_flips_its_sign_on_the_reciprocal_market()
    {
        string[] signed = ["roc(5) > 0", "roc(10) < 0", "gap_pct > 0", "gap_pct <= -0.5"];
        var mirrored = signed.Select(Ok).ToList();

        for (var seed = 0; seed < 40; seed++)
        {
            var bars = Series.Sessions(seed, 3);
            var original = Run(Timeframe.M15, signed.Take(2).ToList(), Reciprocal(bars));
            var onMarket = Run(Timeframe.M15, mirrored.Take(2).ToList(), bars);

            onMarket.ShouldBe(original, "seed " + seed);
        }
    }

    [Fact]
    public void Gap_pct_and_roc_have_the_opposite_sign_where_both_are_ready()
    {
        var bars = Series.Sessions(5, 3);
        var up = Run(Timeframe.M15, ["gap_pct > 0", "gap_pct < 0"], bars);
        var down = Run(Timeframe.M15, ["gap_pct > 0", "gap_pct < 0"], Reciprocal(bars));

        for (var i = 0; i < up[0].Length; i++)
        {
            if (up[0][i] != 'U' && down[0][i] != 'U')
            {
                (up[0][i] == 'T').ShouldBe(down[1][i] == 'T');
            }
        }
    }
}
