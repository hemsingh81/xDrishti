namespace XDrishti.Domain.PairResearch;

/// <summary>The trades of one pair walk and what it counted.</summary>
public sealed record PairWalkResult(IReadOnlyList<PairTrade> Trades, PairWalkCounters Counters);
