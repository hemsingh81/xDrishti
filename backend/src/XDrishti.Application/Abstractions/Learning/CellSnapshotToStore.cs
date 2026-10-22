using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A snapshot of the cell statistics to keep (P6-01, ADR 0064): the rows of every cell and window as of one date, from the stored runs named in <see cref="Sources"/>, under the options in force.</summary>
/// <param name="SnapshotKey">64 lower-case hex characters: SHA-256 of the as-of date, the options and the sources; the same inputs give the same key.</param>
/// <param name="AsOf">The as-of date.</param>
/// <param name="Options">The options the rows were computed with.</param>
/// <param name="Sources">The sources as text, <c>runKey:style:exit</c> joined by commas, in key order.</param>
/// <param name="Rows">One row per cell and window.</param>
/// <param name="CreatedBy">Who made it.</param>
/// <param name="CreatedAt">When.</param>
public sealed record CellSnapshotToStore(string SnapshotKey, DateOnly AsOf, CellStatsOptions Options, string Sources, IReadOnlyList<CellStatsRow> Rows, string CreatedBy, DateTimeOffset CreatedAt);
