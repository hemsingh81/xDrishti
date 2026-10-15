using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The health figures of the syncs (P2-16, ADR 0027), kept in <c>portfolio.broker_sync_state</c> next to the watermarks, and the record of the date windows that dropped rows (<c>portfolio.broker_sync_windows</c>). Every method is one guarded statement; the
/// ones that take part in a window's unit of work (<see cref="NoteWindowAsync"/>) run in the surrounding transaction. Nothing is deleted: the runtime role has no DELETE.
/// </summary>
public interface ISyncHealthStore
{
    /// <summary>The health of one kind; <see cref="SyncHealth.None"/> when there is no row.</summary>
    ValueTask<SyncHealth> GetAsync(Guid accountId, SyncHealthKind kind, CancellationToken cancellationToken);

    /// <summary>The health of every kind of the given accounts that has a row, in one read.</summary>
    ValueTask<IReadOnlyList<SyncHealth>> ListAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    /// <summary>
    /// An attempt failed for <paramref name="reason"/> (a code of <c>SyncFailures</c>) at <paramref name="now"/>: the failed attempts of that India day go up by one, the days in a row go up by one when this is the first failure of a new day, and the
    /// latest failure is noted. Creates the row when there is none.
    /// </summary>
    ValueTask RecordFailureAsync(Guid accountId, SyncHealthKind kind, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>An attempt succeeded: the counters and the latest failure are cleared. Nothing happens (and no row is made) while the kind is healthy.</summary>
    ValueTask RecordSuccessAsync(Guid accountId, SyncHealthKind kind, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The kind was read with the Dhan mappers of <paramref name="version"/> (never lowers the stored version). Creates the row when there is none.</summary>
    ValueTask StampMapperVersionAsync(Guid accountId, SyncHealthKind kind, int version, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>An earlier-history read of the trades was made on India date <paramref name="on"/>: it reached back to <paramref name="deepenedTo"/> (null: it found nothing and the stored value stays) and no earlier year is read any more when <paramref name="exhausted"/>.</summary>
    ValueTask RecordDeepenedAsync(Guid accountId, DateOnly? deepenedTo, bool exhausted, DateOnly on, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Notes how a window was read: the windows noted earlier that lie within it are cleared (they were read again), and when <paramref name="dropped"/> rows were dropped for a data reason the window is noted as needing a new read once the mappers change
    /// (<c>needs_reread</c>). Takes part in the surrounding transaction.
    /// </summary>
    ValueTask NoteWindowAsync(Guid accountId, SyncHealthKind kind, DateOnly from, DateOnly to, int dropped, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The windows that dropped rows for a data reason and have not been read again since, oldest first.</summary>
    ValueTask<IReadOnlyList<BrokerDateRange>> ListRereadAsync(Guid accountId, SyncHealthKind kind, CancellationToken cancellationToken);
}
