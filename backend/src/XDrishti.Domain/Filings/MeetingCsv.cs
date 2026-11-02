namespace XDrishti.Domain.Filings;

/// <summary>
/// The board-meeting csv (P15-04): header exactly <c>symbol,period_end,meeting_date,purpose,status,intimation_ts</c>; at most 20,000 rows. A bad header rejects the file; a bad row is rejected on its own with its line and column and never stops the others.
/// <see cref="Prepare"/> then orders the rows and refuses rows of one (symbol, period, purpose) that carry the same time with different content, all of them, so the outcome never depends on the order of the lines.
/// </summary>
public static class MeetingCsv
{
    public static readonly string[] Header = ["symbol", "period_end", "meeting_date", "purpose", "status", "intimation_ts"];

    public static MeetingParse Parse(string text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(text);
        var records = CsvTable.Parse(text);
        if (records.Count == 0 || records[0].Fields is not { } header || !header.SequenceEqual(Header, StringComparer.Ordinal))
        {
            return new MeetingParse(CalendarRejectReasons.BadHeader, [], []);
        }

        if (records.Count - 1 > CsvCells.MaxRows)
        {
            return new MeetingParse(CalendarRejectReasons.TooManyRows, [], []);
        }

        var rows = new List<MeetingRow>();
        var rejects = new List<RowReject>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields is not { Count: 6 } f)
            {
                rejects.Add(new RowReject(record.Line, CalendarRejectReasons.BadRow));
                continue;
            }

            var symbol = CsvCells.Symbol(f[0]);
            var period = CsvCells.Date(f[1]);
            var meeting = CsvCells.Date(f[2]);
            var purpose = f[3].Trim();
            var status = f[4].Trim();
            var intimation = BroadcastRule.Evaluate(f[5], now);
            var column = symbol is null ? Header[0]
                : period is null ? Header[1]
                : meeting is null ? Header[2]
                : purpose is not (MeetingRow.Results or MeetingRow.Other) ? Header[3]
                : status is not (MeetingRow.Scheduled or MeetingRow.Rescheduled or MeetingRow.Cancelled) ? Header[4]
                : intimation.Reason == BroadcastRule.InFuture ? Header[5]
                : null;
            if (column is not null)
            {
                rejects.Add(new RowReject(record.Line, CalendarRejectReasons.BadRow, column));
                continue;
            }

            rows.Add(new MeetingRow(record.Line, symbol!, period!.Value, meeting!.Value, purpose, status, intimation));
        }

        return new MeetingParse(null, rows, rejects);
    }

    /// <summary>
    /// The rows in loading order (symbol, period, purpose, time with unavailable ones first, then the content) so that a file revises in chain order whatever its line order; and the rows refused because another row of the same (symbol, period, purpose) has the same time but
    /// other content.
    /// </summary>
    public static (IReadOnlyList<MeetingRow> Rows, IReadOnlyList<RowReject> Rejects) Prepare(IEnumerable<MeetingRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var rejects = new List<RowReject>();
        var kept = new List<MeetingRow>();
        foreach (var group in rows.GroupBy(r => (r.Symbol, r.PeriodEnd, r.Purpose)))
        {
            var dated = group.Where(r => r.Time is not null).GroupBy(r => r.Time!.Value.UtcDateTime).ToList();
            var conflicting = dated.Where(g => g.Select(Content).Distinct(StringComparer.Ordinal).Count() > 1).SelectMany(g => g).ToHashSet();
            rejects.AddRange(conflicting.Select(r => new RowReject(r.Line, CalendarRejectReasons.AmbiguousOrder)));
            kept.AddRange(group.Where(r => !conflicting.Contains(r)));
        }

        var ordered = kept
            .OrderBy(r => r.Symbol, StringComparer.Ordinal).ThenBy(r => r.PeriodEnd).ThenBy(r => r.Purpose, StringComparer.Ordinal)
            .ThenBy(r => r.Time?.UtcDateTime ?? DateTime.MinValue).ThenBy(Content, StringComparer.Ordinal).ThenBy(r => r.Line)
            .ToList();
        return (ordered, rejects.OrderBy(r => r.Line).ToList());
    }

    private static string Content(MeetingRow r) => $"{r.MeetingDate:yyyy-MM-dd}|{r.Status}|{r.Intimation.Reason}";
}
