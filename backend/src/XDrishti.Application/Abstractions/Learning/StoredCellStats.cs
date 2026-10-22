using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A stored row with the facts of its snapshot.</summary>
/// <param name="SnapshotKey">The snapshot it belongs to.</param>
/// <param name="CreatedAt">When the snapshot was made.</param>
/// <param name="Options">The options the row was computed with.</param>
/// <param name="Sources">The sources text of the snapshot.</param>
/// <param name="Row">The statistics.</param>
public sealed record StoredCellStats(string SnapshotKey, DateTimeOffset CreatedAt, CellStatsOptions Options, string Sources, CellStatsRow Row);
