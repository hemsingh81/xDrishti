using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// What the frame tests of the calendar and market-context features share: seeded daily contexts on consecutive weekdays (a VIX and a breadth on every day unless asked otherwise, so that a warm-up can be
/// measured exactly), minute bars on given dates, the options that make a short series ready after a few days, and the lookups the tests use as their own independent reading of the context: the latest day strictly
/// before a date found by a plain scan, a column of the batch reference by the name of the feature, and the ISO weekday from the platform's calendar.
/// </summary>
internal static class FrameContextFixtures
{
    /// <summary>A Monday, well before the dates of the minute series of the other tests, so that a default context has warmed up (265 days) by 2026.</summary>
    public static DateOnly Start { get; } = new(2024, 6, 3);

    /// <summary>Short periods and a short percentile window: a series of 60 days is ready after 14 and every regime occurs in it.</summary>
    public static ContextOptions Small { get; } = new(TrendSmaPeriod: 6, TrendSlopeDays: 2, AdxPeriod: 3, AdxMin: 15, AtrPeriod: 3, PercentileWindow: 10);

    /// <summary>The trend takes longer to be ready (34 days) than the volatility (6 days): the regime waits for the trend.</summary>
    public static ContextOptions SlowTrend { get; } = new(TrendSmaPeriod: 30, TrendSlopeDays: 4, AdxPeriod: 3, AdxMin: 15, AtrPeriod: 2, PercentileWindow: 5);

    /// <summary>
    /// Seeded days on consecutive weekdays from <paramref name="start"/> (a Monday): the index moves through phases of different drift and volatility, and — unless switched off — every day has a VIX and a
    /// breadth. The same seed gives the same days.
    /// </summary>
    public static List<ContextDay> Consecutive(int seed, int count, DateOnly? start = null, bool vix = true, bool breadth = true)
    {
        var first = start ?? Start;
        first.DayOfWeek.ShouldBe(DayOfWeek.Monday, "a series on consecutive weekdays starts on a Monday");
        return
        [
            .. ContextSeriesGenerator.Random(seed, count, count).Select((day, i) => day with
            {
                Date = ContextSeriesGenerator.Weekday(first, i),
                VixClose = vix ? day.VixClose ?? Math.Round(12 + i * 7919 % 400 / 20.0, 2) : null,
                Breadth = breadth ? day.Breadth ?? 50 : null,
            }),
        ];
    }

    public static MarketContext Build(IEnumerable<ContextDay> days, ContextOptions? options = null) => MarketContext.Create(days, options).Value;

    /// <summary>
    /// Seeded consecutive weekdays that cover the dates of the bars: they start on a Monday <paramref name="daysBefore"/> calendar days (about 300 weekdays by default, more than the 265 of the default warm-up)
    /// before the first bar and end a week after the last one.
    /// </summary>
    public static List<ContextDay> Covering(IReadOnlyList<Bar> bars, int seed, int daysBefore = 420, bool vix = true, bool breadth = true)
    {
        var first = DateOf(bars[0]).AddDays(-daysBefore);
        var monday = first.AddDays(-((int)first.DayOfWeek + 6) % 7);
        var count = (DateOf(bars[^1]).AddDays(7).DayNumber - monday.DayNumber) / 7 * 5 + 10;
        return Consecutive(seed, count, monday, vix, breadth);
    }

    /// <summary>Every calendar day from the first to the last, both included.</summary>
    public static List<DateOnly> Dates(DateOnly first, DateOnly last)
    {
        var dates = new List<DateOnly>();
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            dates.Add(date);
        }

        return dates;
    }

    /// <summary>One bar at each of the minutes of the day on every date, in time order (a price that does not matter to the date-driven features).</summary>
    public static List<Bar> OnDates(IEnumerable<DateOnly> dates, params int[] minutesOfDay)
    {
        var bars = new List<Bar>();
        foreach (var date in dates)
        {
            foreach (var minute in minutesOfDay)
            {
                bars.Add(Minutes.Close(date, minute, 100 + minute % 7));
            }
        }

        return bars;
    }

    /// <summary>The 375 bars of a regular session, 09:15 to 15:29.</summary>
    public static List<Bar> Session(DateOnly date) => OnDates([date], [.. Enumerable.Range(Minutes.Open, 375)]);

    /// <summary>The India date of a bar, from the epoch with plain arithmetic on the platform's date type (independent of the library's day arithmetic).</summary>
    public static DateOnly DateOf(Bar bar) => DateOnly.FromDateTime(DateTime.UnixEpoch.AddSeconds(bar.EpochSeconds + 19_800));

    /// <summary>The ISO weekday (Monday 1 … Sunday 7) from the platform's day of week.</summary>
    public static int IsoWeekday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    /// <summary>The index of the latest day strictly before the date, found by scanning every day, or -1 when there is none.</summary>
    public static int LatestBefore(IReadOnlyList<ContextDay> days, DateOnly date)
    {
        var found = -1;
        for (var i = 0; i < days.Count; i++)
        {
            if (days[i].Date < date)
            {
                found = i;
            }
        }

        return found;
    }

    /// <summary>The column of the batch reference that a catalog name reads.</summary>
    public static double[] Column(ReferenceContextValues reference, string name) => name switch
    {
        "index_trend" => reference.IndexTrend,
        "index_adx" => reference.IndexAdx,
        "index_vol_pct" => reference.IndexVolPct,
        "vix" => reference.Vix,
        "vix_pct" => reference.VixPct,
        "vix_chg" => reference.VixChg,
        "breadth" => reference.Breadth,
        "regime_trend" => reference.RegimeTrend,
        "regime_vol" => reference.RegimeVol,
        "regime" => reference.Regime,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a market-context feature."),
    };

    public static FeatureSpec[] Specs(IEnumerable<string> names) => [.. names.Select(n => new FeatureSpec(n))];

    /// <summary>A frame of the named date-driven features (each on 1m, no parameters) with the given context and expiry dates (an empty set of them when none is given, so that every name is valid).</summary>
    public static FeatureFrame Frame(IEnumerable<string> names, MarketContext? context, IReadOnlySet<DateOnly>? expiryDays = null, int historyDepth = FrameOptions.DefaultHistoryDepth) =>
        FeatureFrame.Create(Specs(names), new FrameOptions(null, historyDepth, context, expiryDays ?? new HashSet<DateOnly>())).Value;

    /// <summary>Steps the bars and records, after each step, the value and the history lags 1 and 7 of every handle as raw bits: two frames that did the same must give the same trace.</summary>
    public static List<long> Trace(FeatureFrame frame, IEnumerable<Bar> bars)
    {
        var bits = new List<long>();
        foreach (var bar in bars)
        {
            frame.Step(bar);
            for (var h = 0; h < frame.Count; h++)
            {
                bits.Add(BitConverter.DoubleToInt64Bits(frame[h]));
                bits.Add(BitConverter.DoubleToInt64Bits(frame.At(h, 1)));
                bits.Add(BitConverter.DoubleToInt64Bits(frame.At(h, 7)));
            }
        }

        return bits;
    }

    /// <summary>Every history lag the frame keeps (lag 0 is the value), and the ready flag, of every handle as raw bits: two frames that did the same must give the same list.</summary>
    public static List<long> Bits(FeatureFrame frame)
    {
        var bits = new List<long>(frame.Count * (frame.HistoryDepth + 1));
        for (var h = 0; h < frame.Count; h++)
        {
            for (var lag = 0; lag < frame.HistoryDepth; lag++)
            {
                bits.Add(BitConverter.DoubleToInt64Bits(frame.At(h, lag)));
            }

            bits.Add(frame.IsReady(h) ? 1 : 0);
        }

        return bits;
    }
}
