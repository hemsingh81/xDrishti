namespace XDrishti.Application.Reporting;

/// <summary>A pivot of two dimensions by one figure: the row and column values and every cell of the grid, row by row, empty cells included so no gap reads as a zero.</summary>
/// <param name="RowDimension">The dimension of the rows.</param>
/// <param name="ColumnDimension">The dimension of the columns.</param>
/// <param name="Metric">The figure shown (snake case).</param>
/// <param name="Rows">The row values, sorted; null is "not recorded" (last).</param>
/// <param name="Columns">The column values, sorted.</param>
/// <param name="Cells">The cells, row by row.</param>
/// <param name="NonAdditive">A trade appears in more than one cell.</param>
/// <param name="Evidence">The evidence of the report.</param>
public sealed record PivotView(
    string RowDimension,
    string ColumnDimension,
    string Metric,
    IReadOnlyList<string?> Rows,
    IReadOnlyList<string?> Columns,
    IReadOnlyList<PivotCellView> Cells,
    bool NonAdditive,
    ReportEvidenceView Evidence);
