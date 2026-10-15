namespace XDrishti.Application.Portfolio.Reports;

/// <summary>The columns of a report's header row by their normalised names (letters and digits, lower case). Pure.</summary>
internal sealed class HeaderMap
{
    private readonly string[] _names;

    public HeaderMap(IReadOnlyList<string> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _names = [.. cells.Select(ReportCells.Norm)];
    }

    /// <summary>The index of the first column with one of these names; -1 when there is none.</summary>
    public int Find(params string[] aliases) => Array.FindIndex(_names, n => n.Length > 0 && aliases.Contains(n));

    /// <summary>The index of the first row that has all of these column names (within the first <see cref="MaxHeaderRow"/> non-blank rows); -1 when none has.</summary>
    public static int FindHeaderRow(Sheet sheet, params string[] required)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var seen = 0;
        for (var i = 0; i < sheet.Rows.Count && seen < MaxHeaderRow; i++)
        {
            if (sheet.Rows[i].IsBlank)
            {
                continue;
            }

            seen++;
            var map = new HeaderMap(sheet.Rows[i].Cells);
            if (required.All(r => map.Find(r) >= 0))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The title, the personal lines and the header are within this many non-blank rows.</summary>
    public const int MaxHeaderRow = 20;
}
