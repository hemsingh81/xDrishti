namespace XDrishti.Domain.Replay;

/// <summary>One candidate of a replayed session with its place in the ranked list (rank 0 when it never reached the ranking).</summary>
/// <param name="Symbol">The symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="CellKey">The cell.</param>
/// <param name="Rank">The rank in the plan; 0 when not ranked.</param>
public sealed record ReplayCandidateRow(string Symbol, string Side, string CellKey, int Rank);
