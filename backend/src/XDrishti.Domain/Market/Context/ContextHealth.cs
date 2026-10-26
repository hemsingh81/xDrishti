using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Market.Context;

/// <summary>
/// How current the stored context is against the calendar: the last stored date, how many trading sessions it is behind the last closed one (<paramref name="AgeSessions"/>), and how many sessions between the first and the last stored day have no record
/// (<paramref name="GapSessions"/>). Null when nothing is stored. More than one session behind is stale (the end-of-day run writes the closed session the same afternoon).
/// </summary>
public sealed record ContextHealth(DateOnly First, DateOnly Last, int AgeSessions, int GapSessions)
{
    /// <summary>Sessions behind above which the context is called stale.</summary>
    public const int StaleAfterSessions = 1;

    public bool Stale => AgeSessions > StaleAfterSessions;

    public static ContextHealth? Of(IReadOnlyCollection<DateOnly> stored, TradingCalendar calendar, DateOnly lastClosedSession)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(calendar);
        if (stored.Count == 0)
        {
            return null;
        }

        var have = stored as ISet<DateOnly> ?? stored.ToHashSet();
        var first = stored.Min();
        var last = stored.Max();
        var gaps = 0;
        for (var date = first; date < last; date = date.AddDays(1))
        {
            if (calendar.IsTradingDay(date) && !have.Contains(date))
            {
                gaps++;
            }
        }

        return new ContextHealth(first, last, calendar.TradingDaysBetween(last, lastClosedSession), gaps);
    }
}
