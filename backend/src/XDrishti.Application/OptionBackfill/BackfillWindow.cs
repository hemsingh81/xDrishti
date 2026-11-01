using XDrishti.Domain.Market;

namespace XDrishti.Application.OptionBackfill;

/// <summary>The nightly window of the backfill, pure (P14-03 criterion 5): 16:30 to 08:30 India time, every day, so it never competes with the live feed, the chart reads and the chain recorder. The end is exclusive.</summary>
public static class BackfillWindow
{
    public static readonly TimeOnly Start = new(16, 30);

    public static readonly TimeOnly End = new(8, 30);

    public static bool IsOpen(DateTimeOffset now)
    {
        var time = TimeOnly.FromTimeSpan(IndiaTime.ToIst(now).TimeOfDay);
        return time >= Start || time < End;
    }

    /// <summary>The start of the India day <paramref name="now"/> falls in (the daily request cap counts from here).</summary>
    public static DateTimeOffset DayStart(DateTimeOffset now) => new(IndiaTime.Today(now).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);

    /// <summary>
    /// The India date on which the night of <paramref name="now"/> began: the date itself from 16:30 on, the day before until 08:30, and - outside the window - the date of the night that is coming. The window crosses midnight, so "the end of the night" is keyed to this
    /// date and not to the India date of the moment (a run that was ended for the night must not start again at 00:00).
    /// </summary>
    public static DateOnly NightOf(DateTimeOffset now)
    {
        var ist = IndiaTime.ToIst(now);
        var date = DateOnly.FromDateTime(ist.DateTime);
        return TimeOnly.FromTimeSpan(ist.TimeOfDay) < End ? date.AddDays(-1) : date;
    }
}
