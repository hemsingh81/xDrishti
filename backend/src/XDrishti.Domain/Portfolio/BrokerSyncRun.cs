using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// One run of the broker history sync of an account (ADR 0025): the read of its trades or of its ledger for the dates <see cref="FromDate"/> to <see cref="ToDate"/> (India dates), who started it, how it ended and what it did
/// (<see cref="Counts"/>, counts only). Recorded for every run, including failed ones and scheduled runs that were skipped because the account's Dhan token had expired. At most one run (of either kind) or holdings sync per account is
/// <c>running</c>. Never deleted: the runtime role has no DELETE. A run is created as running (or as skipped); it is progressed and ended by the store with single guarded statements (<c>WHERE status = 'running'</c>), so a late writer never overwrites an
/// ended run - the entity has no methods for that on purpose.
/// </summary>
public sealed class BrokerSyncRun : Entity<Guid>
{
    public const int MaxErrorLength = 300;

    public const int MaxStartedByLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private BrokerSyncRun(Guid id, Guid accountId, BrokerSyncKind kind, BrokerSyncTrigger trigger, DateOnly fromDate, DateOnly toDate, string startedBy, DateTimeOffset startedAt)
        : base(id)
    {
        AccountId = accountId;
        Kind = kind;
        Trigger = trigger;
        FromDate = fromDate;
        ToDate = toDate;
        StartedBy = startedBy;
        StartedAt = startedAt;
        Status = SyncStatus.Running;
        Counts = BrokerSyncCounts.None;
    }

    public Guid AccountId { get; private set; }

    public BrokerSyncKind Kind { get; private set; }

    public BrokerSyncTrigger Trigger { get; private set; }

    /// <summary>The first date the run reads.</summary>
    public DateOnly FromDate { get; private set; }

    /// <summary>The last date the run reads (the India date it started on).</summary>
    public DateOnly ToDate { get; private set; }

    public SyncStatus Status { get; private set; }

    public BrokerSyncCounts Counts { get; private set; }

    public string StartedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>A short, safe text: why the run failed or was skipped. Never a token, URL, broker message or trade content.</summary>
    public string? Error { get; private set; }

    public bool IsRunning => Status == SyncStatus.Running;

    public static BrokerSyncRun Start(Guid accountId, BrokerSyncKind kind, BrokerSyncTrigger trigger, DateOnly fromDate, DateOnly toDate, string startedBy, DateTimeOffset now)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A run belongs to an account.", nameof(accountId));
        }

        return new BrokerSyncRun(Guid.CreateVersion7(now), accountId, kind, trigger, fromDate, toDate, Truncate(startedBy?.Trim(), MaxStartedByLength) is { Length: > 0 } actor ? actor : "unknown", now);
    }

    /// <summary>A run that was not attempted: recorded as skipped and finished at once.</summary>
    public static BrokerSyncRun Skipped(Guid accountId, BrokerSyncKind kind, BrokerSyncTrigger trigger, DateOnly date, string startedBy, string reason, DateTimeOffset now)
    {
        var run = Start(accountId, kind, trigger, date, date, startedBy, now);
        run.Status = SyncStatus.Skipped;
        run.FinishedAt = now;
        run.Error = Truncate(reason, MaxErrorLength);
        return run;
    }

    private static string? Truncate(string? text, int max) => text is null || text.Length <= max ? text : text[..max];
}
