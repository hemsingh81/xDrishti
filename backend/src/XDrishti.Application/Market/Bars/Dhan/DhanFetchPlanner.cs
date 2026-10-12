using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Bars.Dhan;

/// <summary>
/// Decides what the Dhan phase asks for (ADR 0016 §2): from two days before the instrument's last stored bar (the overlap reconciles Dhan's bars with the
/// stored ones), or <c>Data:Dhan:InitialDays</c> back when it has none, up to now, in windows of at most 90 days (Dhan's limit per request); plus, for the gap
/// backfill (ADR 0017 §3), windows for the older sessions with missing bars. Independent of HTTP, so every <see cref="IIntradayBarSource"/> asks for the same things.
/// </summary>
public static class DhanFetchPlanner
{
    public const int MaxWindowDays = 90;

    public const int OverlapDays = 2;

    /// <summary>Where the ordinary fetch of the instrument starts: two days before its last stored bar, or the initial days back when it has none (and never at or after <paramref name="now"/>).</summary>
    public static DateTimeOffset StartOf(DateTimeOffset? lastBar, DateTimeOffset now, int initialDays)
    {
        var from = lastBar is { } last ? last.AddDays(-OverlapDays) : now.AddDays(-Math.Max(initialDays, 1));
        return from >= now ? now.AddDays(-OverlapDays) : from;
    }

    /// <summary>The windows for one instrument, oldest first; neighbours share their boundary instant.</summary>
    public static IReadOnlyList<FetchWindow> Windows(DateTimeOffset? lastBar, DateTimeOffset now, int initialDays) => Chunk(StartOf(lastBar, now, initialDays), now);

    /// <summary>Cuts the span into windows of at most <see cref="MaxWindowDays"/> days, oldest first; neighbours share their boundary instant. Empty when it is empty.</summary>
    public static IReadOnlyList<FetchWindow> Chunk(DateTimeOffset from, DateTimeOffset to)
    {
        var windows = new List<FetchWindow>();
        var cursor = from;
        while (cursor < to)
        {
            var end = cursor.AddDays(MaxWindowDays);
            if (end > to)
            {
                end = to;
            }

            windows.Add(new FetchWindow(cursor, end));
            cursor = end;
        }

        return windows;
    }

    /// <summary>
    /// The windows that refetch the given sessions (<paramref name="days"/>, India dates, oldest first): sessions that follow each other in the calendar share a window
    /// (from India midnight of the first to India midnight after the last, but not beyond <paramref name="now"/>), which is cut at 90 days; a session that others do not
    /// follow gets its own.
    /// </summary>
    public static IReadOnlyList<FetchWindow> GapWindows(IReadOnlyList<DateOnly> days, TradingCalendar calendar, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(calendar);
        var windows = new List<FetchWindow>();
        var index = 0;
        while (index < days.Count)
        {
            var first = days[index];
            var last = first;
            while (index + 1 < days.Count && calendar.TradingDaysBetween(last, days[index + 1]) <= 1)
            {
                last = days[++index];
            }

            index++;
            var from = Midnight(first);
            var to = Midnight(last.AddDays(1));
            windows.AddRange(Chunk(from, to > now ? now : to));
        }

        return windows;
    }

    public static IntradayFetchPlan Plan(IReadOnlyList<FetchTarget> targets, DateTimeOffset now, int initialDays, IReadOnlyDictionary<Guid, IReadOnlyList<FetchWindow>>? gapWindows = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return new IntradayFetchPlan([.. targets.Select(t => new IntradayTarget(t.InstrumentId, t.Symbol, t.Type, t.SecurityId, t.Segment, WindowsOf(t, now, initialDays, gapWindows)))]);
    }

    private static IReadOnlyList<FetchWindow> WindowsOf(FetchTarget target, DateTimeOffset now, int initialDays, IReadOnlyDictionary<Guid, IReadOnlyList<FetchWindow>>? gapWindows)
    {
        var ordinary = Windows(target.LastBar, now, initialDays);
        return gapWindows is not null && gapWindows.TryGetValue(target.InstrumentId, out var gaps) && gaps.Count > 0 ? [.. gaps, .. ordinary] : ordinary;
    }

    private static DateTimeOffset Midnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
}
