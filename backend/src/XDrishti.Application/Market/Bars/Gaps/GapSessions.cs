using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Bars.Gaps;

/// <summary>Which sessions are checked for missing bars (ADR 0017 §3).</summary>
internal static class GapSessions
{
    /// <summary>
    /// The latest <paramref name="lookback"/> trading sessions of the calendar that have closed at <paramref name="now"/> (so never a future or still open one) and lie
    /// after the day of the instrument's <paramref name="firstBar"/> (the first day is usually part of a session; before it there was no data to miss), newest first.
    /// None without a first bar.
    /// </summary>
    public static IReadOnlyList<DateOnly> Recent(TradingCalendar calendar, DateTimeOffset now, int lookback, DateTimeOffset? firstBar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (firstBar is not { } first || lookback < 1)
        {
            return [];
        }

        var firstDay = IndiaTime.Today(first);
        var sessions = new List<DateOnly>();
        for (var date = calendar.LastClosedSession(now); sessions.Count < lookback && date > firstDay; date = date.AddDays(-1))
        {
            if (calendar.IsTradingDay(date))
            {
                sessions.Add(date);
            }
        }

        return sessions;
    }
}
