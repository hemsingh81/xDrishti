using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The reaction session R of a results filing (P15-05, pure): the first trading session whose open (its own start, 09:15 India time for an ordinary one) is at or after <c>broadcast + lag</c>. A filing broadcast on Friday at 16:12 reacts on Monday; one broadcast on Monday at 11:00
/// reacts on Tuesday. Null when the calendar holds no session within 400 days (never a guess).
/// </summary>
public static class ReactionSession
{
    private const int LookaheadDays = 400;

    public static DateOnly? Of(DateTimeOffset broadcast, TimeSpan lag, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var known = broadcast + lag;
        var first = IndiaTime.Today(known);
        for (var i = 0; i <= LookaheadDays; i++)
        {
            var date = first.AddDays(i);
            if (calendar.SessionOf(date) is { } session && OpenOf(date, session) >= known)
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>The instant a session closes (its own last minute, 15:30 India time for an ordinary one); null when the date has no session.</summary>
    public static DateTimeOffset? CloseOf(DateOnly date, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return calendar.SessionOf(date) is { } session ? At(date, session.ToMinute) : null;
    }

    private static DateTimeOffset OpenOf(DateOnly date, TradingSession session) => At(date, session.FromMinute);

    private static DateTimeOffset At(DateOnly date, int minute) => new(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute), IndiaTime.Offset);
}
