using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>
/// When the end-of-day run happens (ADR 0017 §2): on every trading day of the calendar, at the configured India time, but never before that day's session has closed
/// (a special session that ends later moves the run to its end). India has no daylight saving, so a fixed offset is exact.
/// </summary>
public static class DataEodSchedule
{
    /// <summary>How far ahead <see cref="NextRun"/> looks for a trading day (a calendar full of holidays must not loop for ever).</summary>
    private const int LookaheadDays = 400;

    /// <summary>The instant of the run on the date; null when the market is closed that day.</summary>
    public static DateTimeOffset? RunTime(TradingCalendar calendar, DateOnly date, TimeOnly timeIst)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (calendar.SessionOf(date) is not { } session)
        {
            return null;
        }

        var close = new TimeOnly(session.ToMinute / 60, session.ToMinute % 60);
        return new DateTimeOffset(date.ToDateTime(timeIst > close ? timeIst : close, DateTimeKind.Unspecified), IndiaTime.Offset);
    }

    /// <summary>The first run strictly after <paramref name="now"/>; null when the calendar has no trading day in the next 400 days.</summary>
    public static DateTimeOffset? NextRun(DateTimeOffset now, TimeOnly timeIst, TradingCalendar calendar)
    {
        var today = IndiaTime.Today(now);
        for (var ahead = 0; ahead <= LookaheadDays; ahead++)
        {
            if (RunTime(calendar, today.AddDays(ahead), timeIst) is { } run && run > now)
            {
                return run;
            }
        }

        return null;
    }

    /// <summary>Whether today is a trading day whose run time has come (the session has closed and the configured time has passed).</summary>
    public static bool IsDue(DateTimeOffset now, TimeOnly timeIst, TradingCalendar calendar) =>
        RunTime(calendar, IndiaTime.Today(now), timeIst) is { } run && now >= run;
}
