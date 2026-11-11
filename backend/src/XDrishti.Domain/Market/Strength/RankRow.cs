using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Market.Strength;

/// <summary>One member after ranking: the volatility-adjusted score and the percent rank, each a number or an honest state.</summary>
public sealed record RankRow(string Symbol, Metric Score, Metric PctRank);
