namespace XDrishti.Domain.Reporting;

/// <summary>One basket as the ranking board reads it (BK-07): its index and its relative strength returns, nothing else.</summary>
/// <param name="Id">The basket.</param>
/// <param name="Index">The equal-weight index (m1b).</param>
/// <param name="RelativeStrength">The relative strength returns (m8), one per window.</param>
public sealed record RankInput(Guid Id, IndexSeries Index, IReadOnlyList<RelativeStrengthReturn> RelativeStrength);
