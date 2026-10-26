using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Context;

/// <summary>The sessions of the calendar a breadth window is read from: the sessions in a range, from the calendar, never from calendar days.</summary>
internal static class BreadthSessions
{
    /// <summary>Calendar days to look back per session of the window: a long holiday run can hide many days, so the look-back is generous.</summary>
    private const int DaysPerSession = 3;

    private const int SlackDays = 14;

    /// <summary>The sessions from enough days before <paramref name="earliest"/> to cover its window, up to <paramref name="latest"/>, ascending.</summary>
    public static IReadOnlyList<DateOnly> Between(TradingCalendar calendar, DateOnly earliest, DateOnly latest, int smaSessions)
    {
        var sessions = new List<DateOnly>();
        for (var date = earliest.AddDays(-(smaSessions * DaysPerSession + SlackDays)); date <= latest; date = date.AddDays(1))
        {
            if (calendar.IsTradingDay(date))
            {
                sessions.Add(date);
            }
        }

        return sessions;
    }

    /// <summary>The first session of the window that ends on <paramref name="date"/>, or the date itself when the sessions do not reach back far enough.</summary>
    public static DateOnly WindowStart(IReadOnlyList<DateOnly> sessions, DateOnly date, int smaSessions)
    {
        var at = sessions.ToList().BinarySearch(date);
        return at >= smaSessions - 1 ? sessions[at - smaSessions + 1] : date;
    }
}
