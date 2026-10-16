namespace XDrishti.Domain.Portfolio;

/// <summary>
/// A date window of an account's trade or ledger read that dropped rows for a data reason - a row with no symbol, an unmapped product, a value that could not be read (P2-16, ADR 0027; <c>portfolio.broker_sync_windows</c>). While <see cref="NeedsReread"/> holds, the
/// window is read again as soon as the Dhan mapping changes (its version goes up), which the identities make safe; a read that covers the window clears the mark. Written with set-based statements inside the window's unit of work; the entity is for reading.
/// Never deleted: the runtime role has no DELETE.
/// </summary>
public sealed class BrokerSyncWindow
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private BrokerSyncWindow(Guid accountId, SyncHealthKind kind, DateOnly fromDate, DateOnly toDate, int dropped, bool needsReread, DateTimeOffset notedAt)
    {
        AccountId = accountId;
        Kind = kind;
        FromDate = fromDate;
        ToDate = toDate;
        Dropped = dropped;
        NeedsReread = needsReread;
        NotedAt = notedAt;
    }

    public Guid AccountId { get; private set; }

    public SyncHealthKind Kind { get; private set; }

    public DateOnly FromDate { get; private set; }

    public DateOnly ToDate { get; private set; }

    /// <summary>How many rows of the window were dropped for a data reason when it was read.</summary>
    public int Dropped { get; private set; }

    public bool NeedsReread { get; private set; }

    public DateTimeOffset NotedAt { get; private set; }
}
