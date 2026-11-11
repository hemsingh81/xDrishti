namespace XDrishti.Domain.Replay;

/// <summary>A candidate that was not taken, with the one reason from the closed list and the code the engine gave (empty when it has none).</summary>
/// <param name="Symbol">The symbol; empty for a cell-level reason (no version).</param>
/// <param name="CellKey">The cell.</param>
/// <param name="Reason">The reason.</param>
/// <param name="Detail">The engine code, for example <c>open_positions:fail</c>.</param>
public sealed record ReplayBlocked(string Symbol, string CellKey, BlockReason Reason, string Detail);
