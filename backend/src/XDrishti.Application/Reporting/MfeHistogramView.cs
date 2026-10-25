namespace XDrishti.Application.Reporting;

/// <summary>Counts by MFE: one bin between each pair of edges, then the open bin at or above the last edge.</summary>
/// <param name="Edges">Edges in R.</param>
/// <param name="Counts">Counts per bin between edges.</param>
/// <param name="AboveCount">Trades at or above the last edge.</param>
public sealed record MfeHistogramView(IReadOnlyList<double> Edges, IReadOnlyList<int> Counts, int AboveCount);
