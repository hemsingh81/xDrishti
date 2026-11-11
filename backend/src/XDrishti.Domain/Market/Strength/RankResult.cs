namespace XDrishti.Domain.Market.Strength;

/// <summary>The ranking of a basket on one day: a row per member, the members that voted and those left out (no score).</summary>
public sealed record RankResult(IReadOnlyList<RankRow> Rows, int Ranked, int NotRanked);
