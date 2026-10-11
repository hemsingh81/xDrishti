using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market;

/// <summary>One refresh of the master list: who started it, how far it is, and what it did. Recorded for every run, including failed and unchanged ones.</summary>
public sealed class RefreshRun : Entity<Guid>
{
    public const int MaxWarnings = 50;
    public const int MaxWarningLength = 300;
    public const int MaxErrorLength = 1000;
    public const int MaxRequestedByLength = 256;
    public const int MaxStampLength = 200;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private RefreshRun(Guid id, RefreshTrigger trigger, bool force, string requestedBy, DateTimeOffset startedAt)
        : base(id)
    {
        Trigger = trigger;
        Force = force;
        RequestedBy = requestedBy;
        StartedAt = startedAt;
        Status = RefreshStatus.Running;
        Phase = RefreshPhase.Downloading;
    }

    public RefreshTrigger Trigger { get; private set; }

    public bool Force { get; private set; }

    public string RequestedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public RefreshStatus Status { get; private set; }

    /// <summary>The phase of a running refresh; null once it has ended.</summary>
    public RefreshPhase? Phase { get; private set; }

    public long RowsRead { get; private set; }

    public string? DhanETag { get; private set; }

    public string? DhanLastModified { get; private set; }

    public string? SectorETag { get; private set; }

    public string? SectorLastModified { get; private set; }

    public int Added { get; private set; }

    public int Updated { get; private set; }

    public int Retired { get; private set; }

    public int Revived { get; private set; }

    public int Unchanged { get; private set; }

    public int SkippedByScope { get; private set; }

    public int Collisions { get; private set; }

    public int SectorMatched { get; private set; }

    public int SectorUnmatched { get; private set; }

    public int FnoUnderlyings { get; private set; }

    public int TotalActive { get; private set; }

    public IReadOnlyDictionary<string, int> KeptByType { get; private set; } = new Dictionary<string, int>();

    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public string? Error { get; private set; }

    /// <summary>The <see cref="RefreshRules.Version"/> this run worked under.</summary>
    public int RulesVersion { get; private set; } = RefreshRules.Version;

    /// <summary>False when the sector list could not be used: the next refresh must not skip an unchanged file, so that sectors get filled in.</summary>
    public bool SectorsApplied { get; private set; }

    public bool IsRunning => Status == RefreshStatus.Running;

    public static RefreshRun Start(RefreshTrigger trigger, bool force, string requestedBy, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), trigger, force, Truncate(requestedBy, MaxRequestedByLength)!, now);

    public void ReportProgress(RefreshPhase phase, long rowsRead)
    {
        if (IsRunning)
        {
            Phase = phase;
            RowsRead = rowsRead;
        }
    }

    /// <summary>The source files were not newer than the last run: nothing is imported, the validators are carried forward.</summary>
    public void MarkUnchanged(DateTimeOffset now, SourceStamp dhan, SourceStamp sector, int totalActive)
    {
        End(now, RefreshStatus.Unchanged);
        SetStamps(dhan, sector);
        SectorsApplied = true; // an unchanged run only follows a run whose sectors were applied
        TotalActive = totalActive;
    }

    public void Succeed(DateTimeOffset now, SourceStamp dhan, SourceStamp sector, RefreshOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        End(now, RefreshStatus.Succeeded);
        SetStamps(dhan, sector);
        SectorsApplied = outcome.SectorsApplied;
        Added = outcome.Added;
        Updated = outcome.Updated;
        Retired = outcome.Retired;
        Revived = outcome.Revived;
        Unchanged = outcome.Unchanged;
        SkippedByScope = outcome.SkippedByScope;
        Collisions = outcome.Collisions;
        SectorMatched = outcome.SectorMatched;
        SectorUnmatched = outcome.SectorUnmatched;
        FnoUnderlyings = outcome.FnoUnderlyings;
        TotalActive = outcome.TotalActive;
        RowsRead = outcome.RowsRead;
        KeptByType = new Dictionary<string, int>(outcome.KeptByType);
        SetWarnings(outcome.Warnings);
    }

    public void Fail(DateTimeOffset now, string error, IReadOnlyList<string>? warnings = null)
    {
        End(now, RefreshStatus.Failed);
        Error = Truncate(error, MaxErrorLength);
        if (warnings is not null)
        {
            SetWarnings(warnings);
        }
    }

    private void End(DateTimeOffset now, RefreshStatus status)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("The refresh has already ended.");
        }

        Status = status;
        FinishedAt = now;
        Phase = null;
    }

    private void SetStamps(SourceStamp dhan, SourceStamp sector)
    {
        DhanETag = Truncate(dhan.ETag, MaxStampLength);
        DhanLastModified = Truncate(dhan.LastModified, MaxStampLength);
        SectorETag = Truncate(sector.ETag, MaxStampLength);
        SectorLastModified = Truncate(sector.LastModified, MaxStampLength);
    }

    private void SetWarnings(IReadOnlyList<string> warnings)
    {
        var kept = warnings.Take(MaxWarnings).Select(w => Truncate(w, MaxWarningLength)!).ToList();
        if (warnings.Count > MaxWarnings)
        {
            kept.Add($"…and {warnings.Count - MaxWarnings} more warnings.");
        }

        Warnings = kept;
    }

    private static string? Truncate(string? text, int max) => text is null || text.Length <= max ? text : text[..max];
}
