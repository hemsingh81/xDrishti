using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>Trading calendars for the tests: the special sessions that a series contains (a Saturday or evening session, a weekday that ends early), as a <see cref="TradingCalendar"/> and as plain windows for the reference.</summary>
internal static class CalendarFixtures
{
    /// <summary>Per India date: the minute window (first bar, last bar + 1) of every session that is not a full regular one.</summary>
    public static Dictionary<DateOnly, (int From, int To)> Windows(IReadOnlyList<Bar> bars)
    {
        var windows = new Dictionary<DateOnly, (int From, int To)>();
        foreach (var day in bars.GroupBy(b => SessionClock.DateOf(b.EpochSeconds)))
        {
            var minutes = day.Select(b => SessionClock.MinuteOfDay(b.EpochSeconds)).ToList();
            var from = minutes.Min();
            var to = minutes.Max() + 1;
            var weekend = day.Key.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var evening = from >= SessionClock.EveningSessionFromMinute;
            if (weekend || evening || to < SessionClock.CloseMinute)
            {
                windows[day.Key] = (from, to);
            }
        }

        return windows;
    }

    /// <summary>The same windows without the Saturday and Sunday sessions: a calendar that knows the early closes but not a special session on a weekend.</summary>
    public static Dictionary<DateOnly, (int From, int To)> WeekdayWindows(IReadOnlyList<Bar> bars) =>
        Windows(bars).Where(w => w.Key.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToDictionary(w => w.Key, w => w.Value);

    public static TradingCalendar For(IReadOnlyList<Bar> bars) => For(Windows(bars));

    /// <summary>The special sessions of the bars and the given weekday holidays.</summary>
    public static TradingCalendar For(IReadOnlyList<Bar> bars, IEnumerable<DateOnly> holidays) => For(Windows(bars), holidays);

    public static TradingCalendar For(IReadOnlyDictionary<DateOnly, (int From, int To)> windows, IEnumerable<DateOnly> holidays) =>
        new(windows.Select(w => TradingDay.Special(w.Key, w.Value.From, w.Value.To, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch))
            .Concat(holidays.Select(h => TradingDay.Holiday(h, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch))));

    public static TradingCalendar For(IReadOnlyDictionary<DateOnly, (int From, int To)> windows) =>
        new(windows.Select(w => TradingDay.Special(w.Key, w.Value.From, w.Value.To, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)));
}
