using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// What the adversarial session tests share (ISS-13): the twelve per-session names written out here, independent of the catalog, the intraday timeframes, small frame builders and a comparison that treats NaN as equal
/// to NaN. The expected values themselves live in the tests, worked out from the definitions.
/// </summary>
internal static class AdversarialFrames
{
    /// <summary>The twelve restarting features, written out by hand (ADR 0035 / ISS-13).</summary>
    public static readonly IReadOnlyList<string> Twelve =
    [
        "vwap", "or_high", "or_low", "today_open", "today_high", "today_low", "prev_day_open", "prev_day_high", "prev_day_low", "prev_day_close", "prev_day_volume", "gap_pct",
    ];

    /// <summary>The six timeframes on which a session feature restarts.</summary>
    public static readonly IReadOnlyList<Timeframe> Intraday = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1];

    public static readonly double Nan = double.NaN;

    public static FeatureSpec Spec(string name, Timeframe timeframe, double p1 = 0, PriceSource source = PriceSource.Default) => new(name, timeframe, source, p1);

    /// <summary>One spec of the name on the timeframe; the opening range of a name <c>or_*</c> is <paramref name="orMinutes"/> long (one bar of the timeframe when 0).</summary>
    public static FeatureSpec TwelveSpec(string name, Timeframe timeframe, int orMinutes = 0) =>
        Spec(name, timeframe, name.StartsWith("or_", StringComparison.Ordinal) ? (orMinutes > 0 ? orMinutes : Math.Max(1, timeframe.Minutes())) : 0);

    public static List<FeatureSpec> TwelveOn(Timeframe timeframe, int orMinutes = 0) => [.. Twelve.Select(n => TwelveSpec(n, timeframe, orMinutes))];

    public static FeatureFrame Create(IReadOnlyList<FeatureSpec> specs, int depth = 8, TradingCalendar? calendar = null)
    {
        var created = FeatureFrame.Create(specs, new FrameOptions(calendar, depth));
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message + " " + string.Join("; ", created.Error.Fields?.Select(f => f.Field + ": " + f.Message) ?? []) : string.Empty);
        return created.Value;
    }

    /// <summary>Every lag 0 to depth − 1 of the handle.</summary>
    public static double[] Lags(FeatureFrame frame, int handle) => [.. Enumerable.Range(0, frame.HistoryDepth).Select(k => frame.At(handle, k))];

    /// <summary>NaN equals NaN; two numbers are equal within 1e-9 (hand-calculated values) — use <see cref="ShouldBeIdentical"/> to compare two runs of the library bit for bit.</summary>
    public static void ShouldBeNear(this double actual, double expected, string because)
    {
        if (double.IsNaN(expected))
        {
            double.IsNaN(actual).ShouldBeTrue($"expected NaN but was {actual:R} ({because})");
            return;
        }

        double.IsNaN(actual).ShouldBeFalse($"expected {expected:R} but was NaN ({because})");
        Math.Abs(actual - expected).ShouldBeLessThanOrEqualTo(1e-9 * Math.Max(1, Math.Abs(expected)), $"expected {expected:R} but was {actual:R} ({because})");
    }

    /// <summary>Every lag of the handle against the hand-calculated list (its length is the history depth).</summary>
    public static void ShouldHaveLags(this FeatureFrame frame, int handle, double[] expected, string because)
    {
        frame.HistoryDepth.ShouldBe(expected.Length, "the list names every lag");
        ShouldBeLags(Lags(frame, handle), expected, because);
    }

    public static void ShouldBeLags(this double[] actual, double[] expected, string because)
    {
        actual.Length.ShouldBe(expected.Length);
        for (var k = 0; k < expected.Length; k++)
        {
            actual[k].ShouldBeNear(expected[k], $"{because}, lag {k}");
        }
    }

    /// <summary>Two frames built from the same specs hold the same numbers at every handle and lag (bit for bit, NaN equal to NaN), the same counters and the same closed-flags.</summary>
    public static void AssertSameState(FeatureFrame actual, FeatureFrame expected, string where)
    {
        if (actual.Steps != expected.Steps || actual.LastBarTime != expected.LastBarTime)
        {
            Assert.Fail($"{where}: steps {actual.Steps} vs {expected.Steps}, last bar time {actual.LastBarTime} vs {expected.LastBarTime}");
        }

        for (var h = 0; h < expected.Count; h++)
        {
            for (var k = 0; k < expected.HistoryDepth; k++)
            {
                var a = actual.At(h, k);
                var e = expected.At(h, k);
                if (!BitConverter.DoubleToInt64Bits(a).Equals(BitConverter.DoubleToInt64Bits(e)) && !(double.IsNaN(a) && double.IsNaN(e)))
                {
                    Assert.Fail($"{where}: {expected.SpecOf(h)} lag {k}: {a:R} vs expected {e:R}");
                }

                if (actual.IsReadyAt(h, k) != expected.IsReadyAt(h, k))
                {
                    Assert.Fail($"{where}: {expected.SpecOf(h)} lag {k}: readiness {actual.IsReadyAt(h, k)} vs expected {expected.IsReadyAt(h, k)}");
                }
            }

            if (actual.IsReady(h) != expected.IsReady(h))
            {
                Assert.Fail($"{where}: {expected.SpecOf(h)}: IsReady {actual.IsReady(h)} vs expected {expected.IsReady(h)}");
            }
        }
    }

    /// <summary>Exactly equal, NaN equal to NaN (two runs of the same code over the same bars).</summary>
    public static void ShouldBeIdentical(this double actual, double expected, string because) =>
        actual.Equals(expected).ShouldBeTrue($"expected {expected:R} but was {actual:R} ({because})");

    public static string StepName(Bar bar)
    {
        var date = SessionClock.DateOf(bar.EpochSeconds);
        var minute = SessionClock.MinuteOfDay(bar.EpochSeconds);
        return $"{date:yyyy-MM-dd} {minute / 60:00}:{minute % 60:00}";
    }

    /// <summary>The bars of a regular full session (375 minutes from 09:15) whose prices are a seeded walk from the price; returns the close.</summary>
    public static double AddSession(List<Bar> bars, Random random, DateOnly date, double price, int startMinute = Minutes.Open, int count = 375, Func<int, bool>? skip = null)
    {
        for (var i = 0; i < count; i++)
        {
            var minute = startMinute + i;
            var open = price;
            var close = Math.Max(1, open + (random.NextDouble() - 0.5) * price * 0.004);
            if (skip?.Invoke(minute) != true)
            {
                var high = Math.Max(open, close) + random.NextDouble() * price * 0.0008;
                var low = Math.Max(0.5, Math.Min(open, close) - random.NextDouble() * price * 0.0008);
                bars.Add(Minutes.Bar(date, minute, open, high, low, close, 1 + random.Next(0, 5_000)));
            }

            price = close;
        }

        return price;
    }

    /// <summary>Three weekdays (Mon 2, Tue 3, Wed 4 March 2026), full sessions, seeded.</summary>
    public static List<Bar> ThreeFullSessions(int seed)
    {
        var random = new Random(seed);
        var bars = new List<Bar>();
        var price = 1_000.0;
        foreach (var date in new[] { BarFixtures.Mon, BarFixtures.Tue, BarFixtures.Wed })
        {
            price = AddSession(bars, random, date, price * (1 + (random.NextDouble() - 0.5) * 0.02));
        }

        return bars;
    }
}
