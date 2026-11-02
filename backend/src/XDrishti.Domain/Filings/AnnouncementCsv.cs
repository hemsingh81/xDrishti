namespace XDrishti.Domain.Filings;

/// <summary>
/// The announcements csv (P15-07): header exactly <c>symbol,broadcast_ts,category,subject,text</c>; at most 20,000 rows. A bad header rejects the file; a bad row is rejected on its own with its line and column and never stops the others. A blank or invalid
/// <c>broadcast_ts</c> is not a reject: the row is stored <c>unavailable</c>, never dated. A cell longer than its limit is <c>too_long</c> (never truncated). Text keeps its characters as given (no Unicode normalisation: the app runs with invariant globalization, where it would silently do nothing) with LF line breaks; a control character (other than tab and line breaks in the text) or a
/// bidirectional override is a bad cell. Pure; the content of a cell is never reported, only its line and column name.
/// </summary>
public static class AnnouncementCsv
{
    public static readonly string[] Header = ["symbol", "broadcast_ts", "category", "subject", "text"];

    public const int MaxCategory = 60;

    public const int MaxSubject = 300;

    public const int MaxText = 4_000;

    public static AnnouncementParse Parse(string text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(text);
        var records = CsvTable.Parse(text);
        if (records.Count == 0 || records[0].Fields is not { } header || !header.SequenceEqual(Header, StringComparer.Ordinal))
        {
            return new AnnouncementParse(CalendarRejectReasons.BadHeader, [], []);
        }

        if (records.Count - 1 > CsvCells.MaxRows)
        {
            return new AnnouncementParse(CalendarRejectReasons.TooManyRows, [], []);
        }

        var rows = new List<AnnouncementRow>();
        var rejects = new List<RowReject>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields is not { Count: 5 } f)
            {
                rejects.Add(new RowReject(record.Line, CalendarRejectReasons.BadRow));
                continue;
            }

            var symbol = CsvCells.Symbol(f[0]);
            var category = Clean(f[2], false);
            var subject = Clean(f[3], false);
            var body = Clean(f[4], true);
            var broadcast = BroadcastRule.Evaluate(f[1], now);
            var bad = symbol is null ? Header[0]
                : category is null || category.Length == 0 ? Header[2]
                : subject is null || subject.Length == 0 ? Header[3]
                : body is null || body.Length == 0 ? Header[4]
                : null;
            if (bad is not null)
            {
                rejects.Add(new RowReject(record.Line, CalendarRejectReasons.BadRow, bad));
                continue;
            }

            var tooLong = category!.Length > MaxCategory ? Header[2] : subject!.Length > MaxSubject ? Header[3] : body!.Length > MaxText ? Header[4] : null;
            if (tooLong is not null)
            {
                rejects.Add(new RowReject(record.Line, CalendarRejectReasons.TooLong, tooLong));
                continue;
            }

            rows.Add(new AnnouncementRow(record.Line, symbol!, broadcast, category, subject!, body!));
        }

        return new AnnouncementParse(null, rows, rejects);
    }

    /// <summary>Trims and, for the text, turns line breaks into LF; null when the cell holds a character that is not allowed.</summary>
    private static string? Clean(string cell, bool multiline)
    {
        var normalised = cell;
        if (multiline)
        {
            normalised = normalised.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        }

        normalised = normalised.Trim();
        foreach (var c in normalised)
        {
            var allowed = multiline && (c == '\n' || c == '\t');
            if (!allowed && (char.IsControl(c) || IsHidden(c)))
            {
                return null;
            }
        }

        return normalised;
    }

    // Bidirectional overrides and isolates, zero-width marks and the line and paragraph separators: they change what a reader sees without changing what the text says.
    private static bool IsHidden(char c) =>
        c is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069') or '\u200B' or '\u200E' or '\u200F' or '\u2028' or '\u2029' or '\uFEFF';
}
