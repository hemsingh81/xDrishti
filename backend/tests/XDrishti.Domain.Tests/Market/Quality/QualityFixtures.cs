using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Domain.Tests.Market.Quality;

/// <summary>Hand-made 1-minute bars and sessions for the quality checks. Monday 2026-10-12 12:00 India time; the window is Monday 5 to Friday 9 October.</summary>
internal static class QualityFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.FromMinutes(330));

    public static readonly DateOnly Mon = new(2026, 10, 5);
    public static readonly DateOnly Tue = new(2026, 10, 6);
    public static readonly DateOnly Wed = new(2026, 10, 7);
    public static readonly DateOnly Thu = new(2026, 10, 8);
    public static readonly DateOnly Fri = new(2026, 10, 9);

    public static readonly IReadOnlyList<DateOnly> Window = [Mon, Tue, Wed, Thu, Fri];

    public static readonly QualityThresholds Thresholds = new(20, 1);

    /// <summary>The start of the minute (minutes since India midnight) on the date, as epoch seconds.</summary>
    public static long Epoch(DateOnly date, int minuteOfDay) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds() + minuteOfDay * 60L;

    public static int Minute(int hour, int minute) => hour * 60 + minute;

    /// <summary>A bar that moves very little: price 100.00, 100.01 or 100.02 by the minute, high and low a cent outside.</summary>
    public static Bar Calm(DateOnly date, int minuteOfDay)
    {
        var open = 100 + 0.01 * (minuteOfDay % 3);
        var close = 100 + 0.01 * ((minuteOfDay + 1) % 3);
        return new Bar(Epoch(date, minuteOfDay), Math.Round(open, 2), Math.Round(Math.Max(open, close) + 0.01, 2), Math.Round(Math.Min(open, close) - 0.01, 2), Math.Round(close, 2), 10);
    }

    /// <summary>A bar with exactly these values.</summary>
    public static Bar Of(DateOnly date, int minuteOfDay, double open, double high, double low, double close, long volume = 10) =>
        new(Epoch(date, minuteOfDay), open, high, low, close, volume);

    /// <summary>A flat bar at the price: open, high, low and close the same.</summary>
    public static Bar Flat(DateOnly date, int minuteOfDay, double price, long volume = 10) => Of(date, minuteOfDay, price, price, price, price, volume);

    /// <summary>Calm bars for the minutes of a regular session (09:15 to 15:29), minus those the predicate leaves out.</summary>
    public static List<Bar> Session(DateOnly date, Func<int, bool>? skip = null) =>
        [.. Enumerable.Range(SessionClock.OpenMinute, SessionClock.RegularBars).Where(m => skip?.Invoke(m) != true).Select(m => Calm(date, m))];

    public static List<Bar> Sessions(params DateOnly[] dates) => [.. dates.SelectMany(d => Session(d))];

    public static List<Bar> FullWindow() => Sessions(Mon, Tue, Wed, Thu, Fri);

    /// <summary>The stored aggregates exactly as the checks recompute them (so a test can then tamper with them).</summary>
    public static List<StoredBucket> StoredFrom(IReadOnlyList<Bar> bars) =>
        [.. MinuteAggregator.Build(bars).Select(b => new StoredBucket(b.Key.Timeframe, b.Key.Start, b.Value.Open, b.Value.High, b.Value.Low, b.Value.Close, b.Value.Volume, b.Value.Bars))];

    public static QualityInput Input(
        IReadOnlyList<Bar> bars,
        IReadOnlyList<StoredBucket>? stored = null,
        TradingCalendar? calendar = null,
        DateTimeOffset? firstBar = null,
        DateTimeOffset? lastBar = null,
        IReadOnlySet<DateOnly>? unfillable = null) =>
        new(
            Now,
            calendar ?? new TradingCalendar(),
            Window,
            firstBar ?? new DateTimeOffset(2026, 9, 1, 9, 15, 0, TimeSpan.FromMinutes(330)), // an instrument with history: no window day is its first
            lastBar ?? DateTimeOffset.FromUnixTimeSeconds(bars.Count == 0 ? 0 : bars.Max(b => b.EpochSeconds)),
            bars,
            stored ?? StoredFrom(bars),
            unfillable ?? new HashSet<DateOnly>());
}
