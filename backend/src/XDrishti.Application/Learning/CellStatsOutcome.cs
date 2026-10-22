namespace XDrishti.Application.Learning;

/// <summary>What a computation did.</summary>
/// <param name="SnapshotKey">The key of the snapshot.</param>
/// <param name="Cells">The number of cells.</param>
/// <param name="Rows">The number of rows (cells times windows).</param>
/// <param name="Trades">The closed trades read.</param>
/// <param name="AlreadyStored">The identical snapshot was there; nothing was written.</param>
public sealed record CellStatsOutcome(string SnapshotKey, int Cells, int Rows, int Trades, bool AlreadyStored);
