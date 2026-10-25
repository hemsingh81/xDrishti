using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// Counts of trades by MAE (rows) and result R (columns). <see cref="MaeEdges"/> are the lower edges of the MAE bins plus the last edge; there is one more row than bins between edges (the last row is "at or above the last edge"). <see cref="ResultEdges"/> likewise; the
/// first column is below the first edge and the last column at or above the last edge. <see cref="BelowCount"/> and <see cref="AboveCount"/> repeat the totals of those two tail columns.
/// </summary>
public sealed record MaeResultGrid(ValueList<double> MaeEdges, ValueList<double> ResultEdges, ValueList<ValueList<int>> Counts, int BelowCount, int AboveCount);
