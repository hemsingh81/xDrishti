using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The calendars of the swing rows (P3-03d, <i>Definitions</i>): dates in the week of Monday 2026-03-02.</summary>
internal static class TestCalendars
{
    public static readonly DateOnly Mon = new(2026, 3, 2);

    public static readonly DateOnly Tue = new(2026, 3, 3);

    public static readonly DateOnly Wed = new(2026, 3, 4);

    public static readonly DateOnly Thu = new(2026, 3, 5);

    public static readonly DateOnly Fri = new(2026, 3, 6);

    public static readonly DateOnly Sat = new(2026, 3, 7);

    public static readonly DateOnly NextMon = new(2026, 3, 9);

    /// <summary>The default week: no exception, every weekday is a regular session.</summary>
    public static TradingCalendar DefaultWeek() => new();

    private static TradingDay Holiday(DateOnly date) => TradingDay.Holiday(date, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);

    private static TradingDay Special(DateOnly date, int from, int to) => TradingDay.Special(date, from, to, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);

    /// <summary>A holiday on Tuesday 2026-03-03.</summary>
    public static TradingCalendar HolidayOnTuesday() => new([Holiday(Tue)]);

    /// <summary>A confirmed special session on Saturday 2026-03-07 from 09:15 to 12:30.</summary>
    public static TradingCalendar SpecialSaturday() => new([Special(Sat, 9 * 60 + 15, 12 * 60 + 30)]);

    /// <summary>Every weekday of the four weeks from 2026-03-03 a holiday: no trading day within 30 days.</summary>
    public static TradingCalendar NoTradingDays(DateOnly after)
    {
        var days = new List<TradingDay>();
        for (var date = after.AddDays(1); date <= after.AddDays(40); date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(Holiday(date));
            }
        }

        return new TradingCalendar(days);
    }

    /// <summary>A calendar from explicit holidays and special sessions (used by the seeded generator).</summary>
    public static TradingCalendar Of(IEnumerable<DateOnly> holidays, IEnumerable<(DateOnly Date, int From, int To)> specials) =>
        new([.. holidays.Select(Holiday), .. specials.Select(s => Special(s.Date, s.From, s.To))]);
}
