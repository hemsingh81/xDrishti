using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>What a computation did.</summary>
/// <param name="SnapshotKey">The key of the snapshot.</param>
/// <param name="Cells">The number of cells.</param>
/// <param name="Rows">The number of rows (cells times windows).</param>
/// <param name="Trades">The closed trades read.</param>
/// <param name="AlreadyStored">The identical snapshot was there; nothing was written.</param>
/// <param name="Regime">How the regime labels of the trades came out; null when the regime split was not asked for (P6-02).</param>
/// <param name="RegimeCells">The regime cells in the snapshot (each has three window rows).</param>
/// <param name="ShrunkRows">CF-02: the rows whose shrunk figures could be computed (0 when the shrinkage is Off).</param>
/// <param name="TauFloorRows">CF-02: of those, the rows whose between-cell variance is the floor rather than an estimate.</param>
public sealed record CellStatsOutcome(string SnapshotKey, int Cells, int Rows, int Trades, bool AlreadyStored, RegimeTally? Regime = null, int RegimeCells = 0, int ShrunkRows = 0, int TauFloorRows = 0)
{
    /// <summary>CF-02: the share of shrunk rows at the tau-squared floor (0 to 1); null when no row was shrunk. A share near 1 says the floor, not the data, sets the shrinkage.</summary>
    public double? TauFloorShare => ShrunkRows == 0 ? null : (double)TauFloorRows / ShrunkRows;
}
