namespace XDrishti.Application.Reporting;

/// <summary>One bin of an R distribution: trades with R from <paramref name="LowerR"/> (included) to <paramref name="UpperR"/> (excluded); a null edge is open.</summary>
/// <param name="LowerR">The lower edge; null for the open bin below the range.</param>
/// <param name="UpperR">The upper edge; null for the open bin above the range.</param>
/// <param name="Count">Trades in the bin.</param>
public sealed record DistributionBinView(double? LowerR, double? UpperR, int Count);
