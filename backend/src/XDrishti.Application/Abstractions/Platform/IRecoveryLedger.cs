namespace XDrishti.Application.Abstractions.Platform;

/// <summary>The states of a ledger item (<c>platform.recovery_items.state</c>).</summary>
public static class RecoveryStates
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Blocked = "blocked";
    public const string Waiting = "waiting";
    public const string Superseded = "superseded";

    /// <summary>The step ran and succeeded but the proof found sessions not recovered (spec AU-03); final for the occurrence, the sessions are named in <c>detail</c> and the reason.</summary>
    public const string Partial = "partial";

    public const string Interrupted = "interrupted";
}

/// <summary>One item of the ledger: one run to make for one service and occurrence (unique per service and occurrence); <c>Detail</c> is the JSON of the proof of its sessions (spec AU-03), null until proved.</summary>
public sealed record RecoveryItemRow(Guid Id, Guid PassId, string ServiceId, string Occurrence, DateOnly SessionFrom, DateOnly SessionTo, string State, string? Reason, int Attempts, Guid? RunId, string? Detail = null);

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

    /// <summary>Items that failed with fewer than <paramref name="maxAttempts"/> attempts, and items that wait (market hours, a skipped run): the next pass tries them again.</summary>
    ValueTask<IReadOnlyList<RecoveryItemRow>> ListRetryableAsync(int maxAttempts, CancellationToken cancellationToken);

    /// <summary>Closes the service's open items (failed, blocked, waiting) of an older occurrence than <paramref name="occurrence"/> as superseded: a later step covers them. A failed item that used up its attempts stays (its failure is the evidence and its notice). Shortcut: a later step is taken to cover every older occurrence, true for the classes in use (convergent, latest-only); a dated service would need its own date.</summary>
    ValueTask SupersedeOlderAsync(string serviceId, string occurrence, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>Inserts the item or, for the same service and occurrence, updates it (the unique key makes a repeat idempotent).</summary>
    ValueTask SaveAsync(RecoveryItemRow item, CancellationToken cancellationToken);

    /// <summary>Items that wait (market hours, a skipped run) for a session on or before <paramref name="sessionOnOrBefore"/>, with the instant of the pass that last touched them.</summary>
    ValueTask<IReadOnlyList<RecoveryExhaustedRow>> ListWaitingAsync(DateOnly sessionOnOrBefore, CancellationToken cancellationToken);

    /// <summary>The items left <c>partial</c> (newest first, at most 50): proved again at every pass and by the data guard.</summary>
    ValueTask<IReadOnlyList<RecoveryItemRow>> ListPartialAsync(CancellationToken cancellationToken);

    /// <summary>Failed items with at least <paramref name="maxAttempts"/> attempts whose pass started at or after <paramref name="since"/>.</summary>
    ValueTask<IReadOnlyList<RecoveryExhaustedRow>> ListExhaustedAsync(int maxAttempts, DateTimeOffset since, CancellationToken cancellationToken);
}
