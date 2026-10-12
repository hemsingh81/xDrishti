using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// The lower bounds, as epoch seconds (India midnight), from which stored aggregates are compared: intraday and day buckets from the first session of the window, week buckets from the Monday
/// of that session's week and month buckets from the 1st of its month (a week or month bucket reaches back before the window, so the 1-minute bars are read from the earlier of the two).
/// </summary>
public readonly record struct QualityBounds(long Window, long Week, long Month)
{
    /// <summary>Where the 1-minute bars must be read from so that every compared bucket is recomputed from all its bars.</summary>
    public long ReadFrom => Math.Min(Window, Math.Min(Week, Month));
}

/// <summary>Which sessions a quality run looks at: the last N trading days of the calendar that have closed.</summary>
public static class QualityWindow
{
    /// <summary>The last <paramref name="count"/> trading sessions of the calendar that have closed at <paramref name="now"/>, oldest first; fewer when the calendar has no more.</summary>
    public static IReadOnlyList<DateOnly> Sessions(TradingCalendar calendar, DateTimeOffset now, int count)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var sessions = new List<DateOnly>();
        var floor = calendar.LastClosedSession(now).AddDays(-3660);
        for (var date = calendar.LastClosedSession(now); sessions.Count < count && date > floor; date = date.AddDays(-1))
        {
            if (calendar.IsTradingDay(date))
            {
                sessions.Add(date);
            }
        }

        sessions.Reverse();
        return sessions;
    }

    public static QualityBounds Bounds(DateOnly firstSession) =>
        new(MinuteAggregator.MidnightOf(firstSession), MinuteAggregator.WeekStartOf(firstSession), MinuteAggregator.MonthStartOf(firstSession));
}
