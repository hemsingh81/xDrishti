using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Research.SimAll;

/// <summary>Why a job waits (spec AU-12). Only <see cref="Owner"/> needs the owner to resume; every other reason lifts itself when its condition clears.</summary>
public static class SimAllPause
{
    /// <summary>The owner paused the job.</summary>
    public const string Owner = "owner";

    /// <summary>It is market hours (acceptance 5).</summary>
    public const string MarketHours = "market_hours";

    /// <summary>Free disk is under the floor (acceptance 6).</summary>
    public const string LowDisk = "low_disk";

    /// <summary>A bar history job is fetching from the broker, so the job leaves the database and the broker alone.</summary>
    public const string DataJob = "data_job";

    /// <summary>The backfill is no longer complete.</summary>
    public const string Backfill = "backfill";

    /// <summary>The margin before the real session opens and after it closes that the job also keeps clear (the pre-open and the close auction: a regular session 09:15 to 15:30 gives 09:00 to 15:45 IST).</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(15);

    /// <summary>The window the job keeps clear on a date, from the real session of the calendar (a special session or a holiday is respected); null on a day with no session.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End)? WindowOf(TradingCalendar calendar, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (calendar.SessionOf(date) is not { } session)
        {
            return null;
        }

        var midnight = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        return (midnight.AddMinutes(session.FromMinute) - Margin, midnight.AddMinutes(session.ToMinute) + Margin);
    }

    /// <summary>True when the instant is inside the window of a session.</summary>
    public static bool InMarketHours(TradingCalendar calendar, DateTimeOffset now) =>
        WindowOf(calendar, IndiaTime.Today(now)) is { } w && now >= w.Start && now < w.End;

    /// <summary>The start of the next window after the instant (looked for ten days ahead, enough to cross a long holiday), or null.</summary>
    public static DateTimeOffset? NextWindowStart(TradingCalendar calendar, DateTimeOffset now)
    {
        var today = IndiaTime.Today(now);
        for (var d = 0; d <= 10; d++)
        {
            if (WindowOf(calendar, today.AddDays(d)) is { } w && w.Start > now)
            {
                return w.Start;
            }
        }

        return null;
    }

    /// <summary>True for a reason the system lifts by itself.</summary>
    public static bool LiftsItself(string? reason) => reason is MarketHours or LowDisk or DataJob or Backfill;
}
