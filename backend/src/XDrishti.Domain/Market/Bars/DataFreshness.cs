namespace XDrishti.Domain.Market.Bars;

/// <summary>
/// How far behind the stored data is. Calendar-free until the holiday calendar (P1-11): the session of a weekday counts as closed from 15:30 India time,
/// weekends have none, and market holidays are not known yet (so an instrument may show as behind on the day after a holiday).
/// </summary>
public static class DataFreshness
{
    /// <summary>The date of the last weekday whose session has closed at <paramref name="now"/> (today after 15:30, otherwise the weekday before).</summary>
    public static DateOnly LastClosedSession(DateTimeOffset now)
    {
        var ist = IndiaTime.ToIst(now);
        var date = DateOnly.FromDateTime(ist.DateTime);
        var minute = ist.Hour * 60 + ist.Minute;
        if (!IsWeekday(date) || minute < SessionClock.CloseMinute)
        {
            date = date.AddDays(-1);
        }

        while (!IsWeekday(date))
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    /// <summary>How many weekday sessions lie between the last bar's date and <paramref name="expected"/> (0 when the data is current or newer).</summary>
    public static int SessionsBehind(DateOnly lastBarDate, DateOnly expected)
    {
        var behind = 0;
        for (var date = lastBarDate.AddDays(1); date <= expected; date = date.AddDays(1))
        {
            if (IsWeekday(date))
            {
                behind++;
            }
        }

        return behind;
    }

    private static bool IsWeekday(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
