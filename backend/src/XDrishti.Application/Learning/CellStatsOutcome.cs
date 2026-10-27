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
public sealed record CellStatsOutcome(string SnapshotKey, int Cells, int Rows, int Trades, bool AlreadyStored, RegimeTally? Regime = null, int RegimeCells = 0);
