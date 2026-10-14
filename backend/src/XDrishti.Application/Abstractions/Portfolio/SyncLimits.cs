namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>How long a sync may be "running" before it counts as interrupted, for the stores that must tell a running sync from a dead one when they decide whether another may start (holdings sync and broker history exclude each other).</summary>
public static class SyncLimits
{
    /// <summary>A holdings sync that has been "running" this long was interrupted (Dhan's timeout is 30 seconds).</summary>
    public static readonly TimeSpan HoldingsStaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>A broker history run that has been "running" this long was interrupted (a 12-month backfill takes minutes).</summary>
    public static readonly TimeSpan BrokerHistoryStaleAfter = TimeSpan.FromMinutes(30);
}
