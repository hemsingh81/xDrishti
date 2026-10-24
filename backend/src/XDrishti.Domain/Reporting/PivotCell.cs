namespace XDrishti.Domain.Reporting;

/// <summary>One cell of a pivot: the count of trades and one figure. A cell no trade falls in is <see cref="MetricState.Empty"/>; a cell below the minimum is masked (<see cref="MetricState.Insufficient"/>) with its count shown.</summary>
/// <param name="Row">The row value; null when not recorded.</param>
/// <param name="Column">The column value; null when not recorded.</param>
/// <param name="N">Trades in the cell.</param>
/// <param name="Value">The figure.</param>
public sealed record PivotCell(string? Row, string? Column, int N, Metric Value);
