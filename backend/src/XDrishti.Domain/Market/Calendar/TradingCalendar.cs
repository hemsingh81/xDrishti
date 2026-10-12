namespace XDrishti.Domain.Market.Calendar;

/// <summary>
/// Which dates had a session (ADR 0017 §1): every weekday, except the confirmed holidays, and every confirmed special session, which may lie on a weekend or a
/// holiday and has its own window. Pure and immutable: built once from the confirmed rows and then asked, so a whole request or job sees one consistent calendar.
/// Proposed and dismissed rows do not count. The session of an ordinary day is 09:15 to 15:30 India time.
/// </summary>
public sealed class TradingCalendar
{
    /// <summary>How far back <see cref="LastClosedSession"/> looks for a trading day (a calendar full of holidays must not loop for ever).</summary>
    private const int LookbackDays = 3660;

    private readonly Dictionary<DateOnly, TradingSession?> _exceptions = [];

    private readonly List<TradingDay> _holidays = [];

    /// <summary>A calendar of the confirmed rows among <paramref name="days"/>; with none, every weekday is a regular session.</summary>
    public TradingCalendar(IEnumerable<TradingDay>? days = null)
    {
        foreach (var day in (days ?? []).Where(d => d.IsConfirmed))
        {
            if (day.Kind == CalendarDayKind.Holiday)
            {
                _exceptions[day.Id] = null;
                _holidays.Add(day);
            }
            else if (day.Window is { } window)
            {
                _exceptions[day.Id] = new TradingSession(day.Id, window.From, window.To);
            }
        }

        _holidays.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    public bool IsTradingDay(DateOnly date) => SessionOf(date) is not null;

    /// <summary>The session of the date; null when the market is closed (a weekend or a holiday that no special session replaces).</summary>
    public TradingSession? SessionOf(DateOnly date) =>
        _exceptions.TryGetValue(date, out var exception) ? exception : IsWeekday(date) ? TradingSession.Regular(date) : null;

    /// <summary>The 1-minute bars a complete session of the date has: 375 for a regular one, the window's length for a special one, 0 when closed.</summary>
    public int ExpectedMinutes(DateOnly date) => SessionOf(date)?.Minutes ?? 0;

    /// <summary>
    /// The latest trading day whose session has closed at <paramref name="now"/>: today once its session is over (15:30 India time, or the end of a special
    /// session; 15:29 is still before), otherwise the trading day before.
    /// </summary>
    public DateOnly LastClosedSession(DateTimeOffset now)
    {
        var ist = IndiaTime.ToIst(now);
        var today = DateOnly.FromDateTime(ist.DateTime);
        var minute = ist.Hour * 60 + ist.Minute;
        if (SessionOf(today) is { } session && minute >= session.ToMinute)
        {
            return today;
        }

        for (var back = 1; back <= LookbackDays; back++)
        {
            var date = today.AddDays(-back);
            if (IsTradingDay(date))
            {
                return date;
            }
        }

        return today.AddDays(-LookbackDays);
    }

    /// <summary>How many trading days lie after <paramref name="after"/> and up to, including, <paramref name="upTo"/> (0 when <paramref name="upTo"/> is not later).</summary>
    public int TradingDaysBetween(DateOnly after, DateOnly upTo)
    {
        var count = 0;
        for (var date = after.AddDays(1); date <= upTo; date = date.AddDays(1))
        {
            if (IsTradingDay(date))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The first confirmed holiday on or after the date; null when none is known.</summary>
    public TradingDay? NextHoliday(DateOnly from) => _holidays.Find(h => h.Id >= from);

    private static bool IsWeekday(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
