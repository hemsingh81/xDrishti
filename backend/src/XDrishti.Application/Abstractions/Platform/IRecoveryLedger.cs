namespace XDrishti.Application.Abstractions.Platform;

/// <summary>The states of a ledger item (<c>platform.recovery_items.state</c>).</summary>
public static class RecoveryStates
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Blocked = "blocked";
    public const string Waiting = "waiting";

    public const string Interrupted = "interrupted";
}

/// <summary>One item of the ledger: one run to make for one service and occurrence (unique per service and occurrence).</summary>
public sealed record RecoveryItemRow(Guid Id, Guid PassId, string ServiceId, string Occurrence, DateOnly SessionFrom, DateOnly SessionTo, string State, string? Reason, int Attempts, Guid? RunId);

/// <summary>A failed item that used up its attempts, with the instant of the pass that last touched it (for the one-notice-per-service-per-day rule).</summary>
public sealed record RecoveryExhaustedRow(string ServiceId, string Occurrence, string? Reason, DateTimeOffset At);

/// <summary>The ledger of recovery passes and items (<c>platform.recovery_passes</c>, <c>recovery_items</c>; ADR 0240). Never deleted from.</summary>
public interface IRecoveryLedger
{
    ValueTask<Guid> BeginPassAsync(string trigger, DateTimeOffset now, CancellationToken cancellationToken);

    ValueTask EndPassAsync(Guid passId, string status, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Items left <c>running</c> by an earlier process become failed (interrupted), and passes left running become interrupted; returns the items.</summary>
    ValueTask<int> InterruptRunningAsync(DateTimeOffset now, CancellationToken cancellationToken);

    ValueTask<RecoveryItemRow?> FindAsync(string serviceId, string occurrence, CancellationToken cancellationToken);

    /// <summary>The newest item (by occurrence) of the service, or null.</summary>
    ValueTask<RecoveryItemRow?> FindNewestAsync(string serviceId, CancellationToken cancellationToken);

    /// <summary>Items that failed and have fewer than <paramref name="maxAttempts"/> attempts: the next pass tries them again.</summary>
    ValueTask<IReadOnlyList<RecoveryItemRow>> ListRetryableAsync(int maxAttempts, CancellationToken cancellationToken);

    /// <summary>Inserts the item or, for the same service and occurrence, updates it (the unique key makes a repeat idempotent).</summary>
    ValueTask SaveAsync(RecoveryItemRow item, CancellationToken cancellationToken);

    /// <summary>Failed items with at least <paramref name="maxAttempts"/> attempts whose pass started at or after <paramref name="since"/>.</summary>
    ValueTask<IReadOnlyList<RecoveryExhaustedRow>> ListExhaustedAsync(int maxAttempts, DateTimeOffset since, CancellationToken cancellationToken);
}
