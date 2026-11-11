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

    /// <summary>
    /// Like <see cref="ListLatestAsync"/> but of the newest snapshot that holds a cash cell, and only its cash rows: a later snapshot of option or pair cells alone does not hide it (P14-06, ADR 0193). The decision paths read this, never <see cref="ListLatestAsync"/>.
    /// </summary>
    ValueTask<IReadOnlyList<StoredCellStats>> ListLatestCashAsync(string? strategy, string? window, CancellationToken cancellationToken);

    /// <summary>The regime rows (regime other than <c>*</c>) of the newest snapshot that has any, optionally of one strategy and one window; empty when no snapshot has them (P6-02). <see cref="ListLatestAsync"/> and the readers that follow it see the <c>*</c> cells only.</summary>
    ValueTask<IReadOnlyList<StoredCellStats>> ListRegimeLatestAsync(string? strategy, string? window, CancellationToken cancellationToken);

    /// <summary>The tier rows (universe <c>liq_high</c>, <c>liq_mid</c> or <c>liq_low</c>) of the newest snapshot that has any, together with the six-part <c>*</c> cash rows of that same snapshot (their parents), optionally of one strategy and one window; empty when no snapshot has tier rows (BK-11, ADR 0261). <see cref="ListLatestAsync"/> and the decision readers see the all-shares cells only.</summary>
    ValueTask<IReadOnlyList<StoredCellStats>> ListUniverseLatestAsync(string? strategy, string? window, CancellationToken cancellationToken);

    /// <summary>Like <see cref="ListLatestAsync"/> but of the newest snapshot whose as-of date is before <paramref name="before"/>: what was known then. The snapshot is chosen among snapshots that hold a cash cell (P14-06): a later snapshot of non-cash cells alone does not hide it; the rows of the chosen snapshot are all returned, so the caller can count the non-cash ones. Empty when there is none.</summary>
    ValueTask<IReadOnlyList<StoredCellStats>> ListLatestBeforeAsync(DateOnly before, string? strategy, string? window, CancellationToken cancellationToken);

    /// <summary>The newest as-of date of any stored snapshot that holds a cash cell (P14-06), or null when there is none (one aggregate read, no rows).</summary>
    ValueTask<DateOnly?> NewestAsOfAsync(CancellationToken cancellationToken);

    /// <summary>The rows (one per window) of one cell in the newest snapshot that holds it; empty when no snapshot holds it.</summary>
    ValueTask<IReadOnlyList<StoredCellStats>> FindLatestAsync(string cellKey, CancellationToken cancellationToken);
}
