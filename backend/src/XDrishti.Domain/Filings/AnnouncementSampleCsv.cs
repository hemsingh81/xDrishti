using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The labelling file (P15-07): <c>id,filing_id,symbol,published_at,category,subject,text,label</c>, every cell quoted, LF line ends, the label blank. <see cref="RowHash"/> is the hash of the seven text columns (line breaks as LF, ends trimmed), so a cell the owner changed
/// is found at import. Pure.
/// </summary>
public static class AnnouncementSampleCsv
{
    public static readonly string[] Header = ["id", "filing_id", "symbol", "published_at", "category", "subject", "text", "label"];

    public static string Time(DateTimeOffset publishedAt) => publishedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    public static string RowHash(string id, string filingId, string symbol, string publishedAt, string category, string subject, string text)
    {
        var body = string.Join('\u001f', [id, filingId, symbol, publishedAt, category, subject, NormalText(text)]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }

    public static string Write(IEnumerable<(SampledAnnouncement Row, AnnouncementBody Body)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var text = new StringBuilder();
        AppendLine(text, Header);
        foreach (var (row, body) in rows)
        {
            AppendLine(text, [row.Id, row.Candidate.FilingId.ToString("D"), row.Candidate.Symbol, Time(row.Candidate.PublishedAt), Escape(body.Category), Escape(body.Subject), Escape(body.Text), string.Empty]);
        }

        return text.ToString();
    }

    /// <summary>The data lines; null when the header is not the one above or a record is malformed (the owner's editor broke the quoting).</summary>
    public static IReadOnlyList<LabelLine>? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var records = CsvTable.Parse(text);
        if (records.Count == 0 || records[0].Fields is not { } header || !header.SequenceEqual(Header, StringComparer.Ordinal))
        {
            return null;
        }

        var lines = new List<LabelLine>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields is not { Count: 8 } f)
            {
                return null;
            }

            lines.Add(new LabelLine(record.Line, f[0], f[1], f[2], f[3], Unescape(f[4]), Unescape(f[5]), Unescape(f[6]), f[7].Trim()));
        }

        return lines;
    }

    /// <summary>
    /// A cell a spreadsheet would run as a formula (it starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return) gets a leading apostrophe in the exported file, and so does one that already starts with apostrophes before such a character, so that
    /// <see cref="Unescape"/> undoes exactly this. The hashes and the labelled set always use the clean value.
    /// </summary>
    public static string Escape(string cell) => Risky(cell) ? "'" + cell : cell;

    /// <summary>Takes one leading apostrophe off a cell that <see cref="Escape"/> would have given one; a spreadsheet that dropped the apostrophe itself leaves a cell that is already clean.</summary>
    public static string Unescape(string cell) => cell.Length > 1 && cell[0] == '\'' && Risky(cell[1..]) ? cell[1..] : cell;

    private static bool Risky(string cell)
    {
        var i = 0;
        while (i < cell.Length && cell[i] == '\'')
        {
            i++;
        }

        return i < cell.Length && cell[i] is '=' or '+' or '-' or '@' or '\t' or '\r';
    }

    /// <summary>The text as hashed and exported: LF line ends, ends trimmed.</summary>
    public static string NormalText(string text) => text.ReplaceLineEndings("\n").Trim();

    private static void AppendLine(StringBuilder builder, string[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            builder.Append(i == 0 ? string.Empty : ",").Append('"').Append(cells[i].Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }

        builder.Append('\n');
    }
}
