using System.Buffers;
using System.Globalization;
using System.Text;

namespace XDrishti.SimExport.Csv;

/// <summary>
/// The body of an export file (P8-20): a header row of column names and then the rows, streamed to the sink of the host as UTF-8 (no byte order mark, line feed ends, RFC 4180 quoting, like <see cref="TradesCsv"/>). Every cell goes through <see cref="CsvRow"/>, so text is
/// defused against formula injection and numbers are written from numeric types only. A row with a different number of cells than the header is a programming error and throws.
/// </summary>
public sealed class ReportCsvBody
{
    private readonly IBufferWriter<byte> _sink;
    private readonly CsvRow _row = new();
    private readonly int _columns;

    /// <summary>Starts the body: writes the header.</summary>
    /// <param name="sink">Where the bytes go; the host owns it.</param>
    /// <param name="columns">The column names (safe names of the file format, written as they are).</param>
    public ReportCsvBody(IBufferWriter<byte> sink, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(columns);
        _sink = sink;
        _columns = columns.Count;
        _row.Begin();
        foreach (var column in columns)
        {
            _row.Raw(column);
        }

        _row.End(sink);
    }

    /// <summary>The data rows written so far (the header is not counted).</summary>
    public int Rows { get; private set; }

    /// <summary>Writes one data row.</summary>
    /// <param name="cells">One cell for each column.</param>
    public void Add(ReadOnlySpan<ExportCell> cells)
    {
        if (cells.Length != _columns)
        {
            throw new ArgumentException($"A row has {cells.Length} cells, the header {_columns}.", nameof(cells));
        }

        _row.Begin();
        foreach (var cell in cells)
        {
            cell.WriteTo(_row);
        }

        _row.End(_sink);
        Rows++;
    }
}

/// <summary>
/// The evidence block of an export file (P8-20): comment lines that begin <c># </c>, before the table and one after it. Every line is cleaned first: CR, LF, TAB and every control or format character become a space (never dropped, so words do not join), and a comma
/// and a quote become a semicolon and an apostrophe, so that a line is exactly one cell to a strict reader, begins with <c>#</c> and can neither end the comment and start a data row nor put a formula in a second cell.
/// </summary>
public static class ReportCsvEvidence
{
    /// <summary>The line that closes a file; its absence shows a file cut short.</summary>
    public static string EndLine(int rows) => string.Create(CultureInfo.InvariantCulture, $"# end of export, rows {rows}\n");

    /// <summary>Writes the comment lines of the block, in order, one per entry.</summary>
    /// <param name="sink">Where the bytes go.</param>
    /// <param name="lines">The lines without the leading marker.</param>
    public static void WriteLines(IBufferWriter<byte> sink, IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(lines);
        foreach (var line in lines)
        {
            Write(sink, "# " + Clean(line) + "\n");
        }
    }

    /// <summary>Writes the closing line.</summary>
    public static void WriteEnd(IBufferWriter<byte> sink, int rows)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Write(sink, EndLine(rows));
    }

    /// <summary>The text of a comment line made safe (see the type summary).</summary>
    public static string Clean(string? text)
    {
        var builder = new StringBuilder(text?.Length ?? 0);
        foreach (var rune in (text ?? string.Empty).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                builder.Append(' ');
            }
            else if (rune.Value == ',')
            {
                builder.Append(';');
            }
            else if (rune.Value == '"')
            {
                builder.Append('\'');
            }
            else
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }

    private static void Write(IBufferWriter<byte> sink, string line)
    {
        var span = sink.GetSpan(Encoding.UTF8.GetMaxByteCount(line.Length));
        sink.Advance(Encoding.UTF8.GetBytes(line, span));
    }
}
