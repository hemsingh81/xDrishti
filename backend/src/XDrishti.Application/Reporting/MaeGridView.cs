namespace XDrishti.Application.Reporting;

/// <summary>Counts by MAE (rows; the last row is the last edge and above) and result R (columns; the first is below the first edge, the last at or above the last edge). The two counts repeat the totals of the tail columns.</summary>
/// <param name="MaeEdges">Edges of the MAE bins in R.</param>
/// <param name="ResultEdges">Edges of the result bins in R.</param>
/// <param name="Counts">Rows by columns.</param>
/// <param name="BelowCount">Trades below the first result edge.</param>
/// <param name="AboveCount">Trades at or above the last result edge.</param>
public sealed record MaeGridView(IReadOnlyList<double> MaeEdges, IReadOnlyList<double> ResultEdges, IReadOnlyList<IReadOnlyList<int>> Counts, int BelowCount, int AboveCount);
