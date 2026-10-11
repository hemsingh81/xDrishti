using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// One run of the holdings sync of an account (manual, automatic or an import): who started it, how it ended and what it did. Recorded for
/// every run, including failed ones and automatic runs that were skipped because the account was not connected. At most one run per account is
/// <c>running</c>.
/// </summary>
public sealed class HoldingSync : Entity<Guid>
{
    public const int MaxErrorLength = 300;

    public const int MaxRequestedByLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private HoldingSync(Guid id, Guid accountId, SyncTrigger trigger, string requestedBy, DateTimeOffset startedAt)
        : base(id)
    {
        AccountId = accountId;
        Trigger = trigger;
        RequestedBy = requestedBy;
        StartedAt = startedAt;
        Status = SyncStatus.Running;
    }

    public Guid AccountId { get; private set; }

    public SyncTrigger Trigger { get; private set; }

    public string RequestedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public SyncStatus Status { get; private set; }

    public int NewCount { get; private set; }

    public int IncreasedCount { get; private set; }

    public int DecreasedCount { get; private set; }

    public int ClosedCount { get; private set; }

    public int UnchangedCount { get; private set; }

    /// <summary>A short, safe text: why the run failed or was skipped. Never a token, URL or broker message.</summary>
    public string? Error { get; private set; }

    public bool IsRunning => Status == SyncStatus.Running;

    public SyncCounts Counts => new(NewCount, IncreasedCount, DecreasedCount, ClosedCount, UnchangedCount);

    public static HoldingSync Start(Guid accountId, SyncTrigger trigger, string requestedBy, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), accountId, trigger, Truncate(requestedBy, MaxRequestedByLength)!, now);

    /// <summary>A run that was not attempted: recorded as skipped and finished at once.</summary>
    public static HoldingSync Skipped(Guid accountId, SyncTrigger trigger, string requestedBy, string reason, DateTimeOffset now)
    {
        var run = Start(accountId, trigger, requestedBy, now);
        run.Status = SyncStatus.Skipped;
        run.FinishedAt = now;
        run.Error = Truncate(reason, MaxErrorLength);
        return run;
    }

    public void Succeed(DateTimeOffset now, SyncCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        End(now, SyncStatus.Succeeded);
        NewCount = counts.Added;
        IncreasedCount = counts.Increased;
        DecreasedCount = counts.Decreased;
        ClosedCount = counts.Closed;
        UnchangedCount = counts.Unchanged;
    }

    /// <summary>Ends a started run that was held back: nothing was changed and the reason is kept.</summary>
    public void Skip(DateTimeOffset now, string reason)
    {
        End(now, SyncStatus.Skipped);
        Error = Truncate(reason, MaxErrorLength);
    }

    public void Fail(DateTimeOffset now, string error)
    {
        End(now, SyncStatus.Failed);
        Error = Truncate(error, MaxErrorLength);
    }

    private void End(DateTimeOffset now, SyncStatus status)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("The sync has already ended.");
        }

        Status = status;
        FinishedAt = now;
    }

    private static string? Truncate(string? text, int max) => text is null || text.Length <= max ? text : text[..max];
}
