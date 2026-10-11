using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Refresh;

/// <summary>One refresh run. <c>trigger</c>: manual · daily · forced; <c>status</c>: running · succeeded · unchanged · failed; <c>phase</c> (while running): downloading · parsing · enriching · saving.</summary>
public sealed record RefreshRunView(
    Guid Id,
    string Trigger,
    string Status,
    string? Phase,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string RequestedBy,
    long RowsRead,
    int Added,
    int Updated,
    int Retired,
    int Revived,
    int Unchanged,
    int SkippedByScope,
    int Collisions,
    int SectorMatched,
    int SectorUnmatched,
    int FnoUnderlyings,
    int TotalActive,
    IReadOnlyDictionary<string, int> KeptByType,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public static RefreshRunView From(RefreshRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new RefreshRunView(
            run.Id,
            run.Trigger.ToString().ToLowerInvariant(),
            run.Status.ToString().ToLowerInvariant(),
            run.Phase?.ToString().ToLowerInvariant(),
            run.StartedAt,
            run.FinishedAt,
            run.RequestedBy,
            run.RowsRead,
            run.Added,
            run.Updated,
            run.Retired,
            run.Revived,
            run.Unchanged,
            run.SkippedByScope,
            run.Collisions,
            run.SectorMatched,
            run.SectorUnmatched,
            run.FnoUnderlyings,
            run.TotalActive,
            run.KeptByType,
            run.Warnings,
            run.Error);
    }
}

/// <summary>The daily automatic refresh: whether it is on, at what India time, and when it runs next.</summary>
public sealed record AutoRefreshView(bool Enabled, string TimeIst, DateTimeOffset? NextRunAt);

/// <summary>Where the master list stands: the refresh in progress (if any), the last finished one, the automatic schedule and how many instruments are active.</summary>
public sealed record RefreshStatusView(RefreshRunView? Current, RefreshRunView? Last, AutoRefreshView AutoRefresh, int TotalActive);

/// <summary>Starts a refresh. <c>force</c> skips the "file unchanged" check.</summary>
public sealed record StartRefreshRequest(bool Force = false);

/// <summary>The refresh that was registered; follow it with <c>GET /api/instruments/refresh/status</c>.</summary>
public sealed record RefreshStarted(Guid RefreshId);
