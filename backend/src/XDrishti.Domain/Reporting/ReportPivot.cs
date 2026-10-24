using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>A pivot of two dimensions by one figure: the row and column values and every cell of the grid (row by row), empty cells included so no gap reads as a zero.</summary>
/// <param name="RowDimension">The dimension of the rows.</param>
/// <param name="ColumnDimension">The dimension of the columns.</param>
/// <param name="Metric">The figure shown.</param>
/// <param name="Rows">The row values, sorted; null is "not recorded" (last).</param>
/// <param name="Columns">The column values, sorted.</param>
/// <param name="Cells">The cells, row by row, <c>Rows.Count * Columns.Count</c> of them.</param>
/// <param name="NonAdditive">A trade appears in more than one cell.</param>
/// <param name="Evidence">The evidence of the whole report.</param>
public sealed record ReportPivot(
    ReportDimension RowDimension,
    ReportDimension ColumnDimension,
    ReportMetric Metric,
    ValueList<string?> Rows,
    ValueList<string?> Columns,
    ValueList<PivotCell> Cells,
    bool NonAdditive,
    ReportEvidence Evidence);
