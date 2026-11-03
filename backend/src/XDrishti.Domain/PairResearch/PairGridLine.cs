namespace XDrishti.Domain.PairResearch;

/// <summary>One line of the variant grid: the variant, its trades and mean R (null when it has none). Reported with N; only the primary variant is judged.</summary>
public sealed record PairGridLine(string Variant, int Trades, double? MeanR);
