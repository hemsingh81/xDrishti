namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// Where cell statistics are kept (P6-01, ADR 0064): <c>research.cell_stats</c> behind a port. Insert-only snapshots; the snapshot key is the idempotency key. Reads see the newest snapshot (by as-of date, then creation time).
/// </summary>
public interface ICellStatsStore
{
    /// <summary>Writes every row of the snapshot in one transaction; false (nothing written) when the snapshot key is stored already.</summary>
    ValueTask<bool> StoreAsync(CellSnapshotToStore snapshot, CancellationToken cancellationToken);

    /// <summary>The <c>*</c> rows of the newest snapshot (the regime cells of P6-02 are read by <see cref="ListRegimeLatestAsync"/>), optionally of one strategy and one window code; ordered by cell key then window.</summary>
    ValueTask<IReadOnlyList<StoredCellStats>> ListLatestAsync(string? strategy, string? window, CancellationToken cancellationToken);

    /// <summary>The regime rows (regime other than <c>*</c>) of the newest snapshot that has any, optionally of one strategy and one window; empty when no snapshot has them (P6-02). <see cref="ListLatestAsync"/> and the readers that follow it see the <c>*</c> cells only.</summary>
    ValueTask<IReadOnlyList<StoredCellStats>> ListRegimeLatestAsync(string? strategy, string? window, CancellationToken cancellationToken);

    /// <summary>Like <see cref="ListLatestAsync"/> but of the newest snapshot whose as-of date is before <paramref name="before"/>: what was known then. Empty when there is none.</summary>
    ValueTask<IReadOnlyList<StoredCellStats>> ListLatestBeforeAsync(DateOnly before, string? strategy, string? window, CancellationToken cancellationToken);

    /// <summary>The newest as-of date of any stored snapshot, or null when there is none (one aggregate read, no rows).</summary>
    ValueTask<DateOnly?> NewestAsOfAsync(CancellationToken cancellationToken);

    /// <summary>The rows (one per window) of one cell in the newest snapshot that holds it; empty when no snapshot holds it.</summary>
    ValueTask<IReadOnlyList<StoredCellStats>> FindLatestAsync(string cellKey, CancellationToken cancellationToken);
}
