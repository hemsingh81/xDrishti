namespace XDrishti.Application.Reporting;

/// <summary>One cell of a pivot: the trade count and one figure. A cell with no trade is <c>empty</c>, a cell below the minimum is <c>insufficient</c> (masked, with its count), never a zero.</summary>
/// <param name="Row">The row value; null when not recorded.</param>
/// <param name="Column">The column value; null when not recorded.</param>
/// <param name="N">Trades in the cell.</param>
/// <param name="Value">The figure.</param>
public sealed record PivotCellView(string? Row, string? Column, int N, MetricView Value);
