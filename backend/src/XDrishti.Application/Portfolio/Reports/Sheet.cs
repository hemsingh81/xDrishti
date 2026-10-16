namespace XDrishti.Application.Portfolio.Reports;

/// <summary>One row of a downloaded report as text cells: the row number in the file (from 1) and the cells, trimmed of nothing (the readers trim what they need).</summary>
internal sealed record SheetRow(int Line, IReadOnlyList<string> Cells)
{
    /// <summary>True when every cell is empty or white space.</summary>
    public bool IsBlank => Cells.All(c => string.IsNullOrWhiteSpace(c));

    /// <summary>The cell at the index, or the empty text beyond the row's end.</summary>
    public string At(int index) => index >= 0 && index < Cells.Count ? Cells[index] : string.Empty;
}

/// <summary>A downloaded report as rows of text cells, whatever its file format (CSV, <c>.xls</c>, <c>.xlsx</c>).</summary>
internal sealed record Sheet(IReadOnlyList<SheetRow> Rows)
{
    /// <summary>The first <paramref name="count"/> rows that are not blank (the title, the header and the lines before it).</summary>
    public IEnumerable<SheetRow> Leading(int count) => Rows.Where(r => !r.IsBlank).Take(count);
}
