using XDrishti.Domain.Market;

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

    /// <summary>The first minute of the session window (09:00 IST); the cash market opens at 09:15 and closes 15:30, the window adds the pre-open and a margin.</summary>
    public static readonly TimeSpan MarketOpen = TimeSpan.FromHours(9);

    /// <summary>The end of the window (15:45 IST).</summary>
    public static readonly TimeSpan MarketClose = new(15, 45, 0);

    /// <summary>True when the instant is inside the market-hours window on a trading day.</summary>
    public static bool InMarketHours(DateTimeOffset now, bool tradingDay)
    {
        var time = IndiaTime.ToIst(now).TimeOfDay;
        return tradingDay && time >= MarketOpen && time < MarketClose;
    }

    /// <summary>True for a reason the system lifts by itself.</summary>
    public static bool LiftsItself(string? reason) => reason is MarketHours or LowDisk or DataJob or Backfill;
}
