using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Records of holdings syncs. At most one run per account is <c>running</c> at any time, across all instances.</summary>
public interface IHoldingSyncStore
{
    /// <summary>
    /// Registers <paramref name="run"/> as the running sync of its account, unless one is running already (a run older than
    /// <paramref name="staleAfter"/> counts as interrupted and is marked failed). False when another sync of the account is running.
    /// Serialised by a database advisory lock so two instances never overlap.
    /// </summary>
    ValueTask<bool> TryStartAsync(HoldingSync run, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Ends a running sync. Takes part in the surrounding transaction. Nothing happens when the run is not running (any more).</summary>
    ValueTask CompleteAsync(Guid id, SyncStatus status, SyncCounts counts, string? error, DateTimeOffset finishedAt, CancellationToken cancellationToken);

    /// <summary>Stores a run that was not attempted (finished at once).</summary>
    ValueTask RecordSkippedAsync(HoldingSync run, CancellationToken cancellationToken);

    /// <summary>Removes a run that is still running without a trace (a run that found nothing worth a record). Takes part in the surrounding transaction.</summary>
    ValueTask DiscardAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Removes the skipped automatic runs of the account within [<paramref name="from"/>, <paramref name="to"/>) except <paramref name="exceptId"/>: one skipped record per day.</summary>
    ValueTask DeleteOtherSkippedAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to, Guid exceptId, CancellationToken cancellationToken);

    /// <summary>Deletes up to <paramref name="batch"/> finished runs (succeeded or skipped, never failed or running) that started before <paramref name="olderThan"/>; returns how many.</summary>
    ValueTask<int> PruneAsync(DateTimeOffset olderThan, int batch, CancellationToken cancellationToken);

    /// <summary>True when an automatic run of the account was skipped within [<paramref name="from"/>, <paramref name="to"/>).</summary>
    ValueTask<bool> HasSkippedBetweenAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The latest run of the account (skipped ones included), or, when <paramref name="accountId"/> is null, of the accounts that count in totals (active, Portfolio role on, included in totals).</summary>
    ValueTask<HoldingSync?> GetLastAsync(Guid? accountId, CancellationToken cancellationToken);

    /// <summary>One run, for reading only.</summary>
    ValueTask<HoldingSync?> GetAsync(Guid id, CancellationToken cancellationToken);

    ValueTask<bool> IsRunningAsync(Guid accountId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<Guid, SyncActivity>> ActivityAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    /// <summary>Marks every run still <c>running</c> that started before <paramref name="processStartedAt"/> as failed (interrupted); returns how many.</summary>
    ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken);
}
