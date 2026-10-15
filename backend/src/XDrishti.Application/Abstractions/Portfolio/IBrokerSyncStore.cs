using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The runs and watermarks of the broker history sync (ADR 0025). At most one run of any kind per account is <c>running</c>, and none while a holdings sync of the account runs (and the holdings sync does not start while one runs here), across all
/// instances. Runs are never deleted. <see cref="UpdateProgressAsync"/> and <see cref="MoveWatermarkAsync"/> take part in the surrounding transaction.
/// </summary>
public interface IBrokerSyncStore
{
    /// <summary>
    /// Registers <paramref name="run"/> as the running sync of its account, unless a holdings sync or another broker history run of the account is running (a run older than <paramref name="staleAfter"/> counts as interrupted and is marked failed).
    /// False when another sync of the account is running. Serialised by the advisory lock the holdings sync uses, so two instances never overlap.
    /// </summary>
    ValueTask<bool> TryStartAsync(BrokerSyncRun run, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Stores a run that was not attempted (finished at once).</summary>
    ValueTask RecordSkippedAsync(BrokerSyncRun run, CancellationToken cancellationToken);

    /// <summary>Saves how far a running run is. Takes part in the surrounding transaction. Nothing happens when the run is not running (any more).</summary>
    ValueTask UpdateProgressAsync(Guid runId, BrokerSyncCounts counts, CancellationToken cancellationToken);

    /// <summary>Ends a running run with its final counts. Takes part in the surrounding transaction. Nothing happens when the run is not running (any more).</summary>
    ValueTask CompleteAsync(Guid runId, SyncStatus status, BrokerSyncCounts counts, string? error, DateTimeOffset finishedAt, CancellationToken cancellationToken);

    /// <summary>The last date read for the account and kind; null when nothing was read yet.</summary>
    ValueTask<DateOnly?> GetWatermarkAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the watermark forward to <paramref name="to"/>, the last date of the window that was just stored, which starts at <paramref name="from"/>. The watermark means "every date up to here was read", so it only moves when the window
    /// starts at or before the day after it (or when there is no watermark yet: a first read from a chosen date); a window that would leave a hole, or a date that is not later, changes nothing. Takes part in the surrounding transaction.
    /// </summary>
    ValueTask MoveWatermarkAsync(Guid accountId, BrokerSyncKind kind, DateOnly from, DateOnly to, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The latest run of the account and kind (skipped ones included); null when there is none.</summary>
    ValueTask<BrokerSyncRun?> GetLastAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken);

    /// <summary>The latest run of the account and kind that succeeded (so one that was skipped, failed or is still running does not count); null when there is none.</summary>
    ValueTask<BrokerSyncRun?> GetLastSucceededAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken);

    /// <summary>The earliest start date of the account's runs of the kind that succeeded: how far back the kind was first read (an earlier-history read of a run is not in it); null when no run succeeded yet.</summary>
    ValueTask<DateOnly?> GetEarliestReadAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken);

    /// <summary>The latest run that succeeded, of each of the given accounts, for the kind; accounts without one are absent. One read for all the accounts (the notices ask for every account at once).</summary>
    ValueTask<IReadOnlyDictionary<Guid, BrokerSyncRun>> ListLastSucceededAsync(IReadOnlyCollection<Guid> accountIds, BrokerSyncKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// The dates of the account's succeeded runs of the kind that dropped rows for a data reason (rows that could not be used, or that no security could be found for) - what the first version of ADR 0027 has to read again, because windows were not noted
    /// before it. Merged where they overlap, oldest first.
    /// </summary>
    ValueTask<IReadOnlyList<BrokerDateRange>> ListDroppedRangesAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken);

    /// <summary>The latest run of the account and kind that has ended, whether it succeeded or failed (a skipped one never read anything and does not count), other than <paramref name="excludedRunId"/>; null when there is none.</summary>
    ValueTask<BrokerSyncRun?> GetPreviousFinishedAsync(Guid accountId, BrokerSyncKind kind, Guid excludedRunId, CancellationToken cancellationToken);

    /// <summary>One run, for reading only.</summary>
    ValueTask<BrokerSyncRun?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>True while a run of the account is running.</summary>
    ValueTask<bool> IsRunningAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Marks every run still <c>running</c> that started before <paramref name="processStartedAt"/> as failed (interrupted); returns how many.</summary>
    ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken);
}
