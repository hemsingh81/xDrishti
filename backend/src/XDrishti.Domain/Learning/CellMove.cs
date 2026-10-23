namespace XDrishti.Domain.Learning;

/// <summary>One changelog row of a cell, as the replay reads it (spec P6-08).</summary>
/// <param name="Id">The row id; rows are replayed in id order.</param>
/// <param name="From">The state the row says the cell was in.</param>
/// <param name="To">The state the row leaves the cell in.</param>
/// <param name="RevertsId">The row this one reverts, or null.</param>
public sealed record CellMove(long Id, CellState From, CellState To, long? RevertsId);
