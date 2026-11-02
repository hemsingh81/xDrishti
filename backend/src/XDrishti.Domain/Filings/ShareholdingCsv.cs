namespace XDrishti.Domain.Filings;

/// <summary>
/// The shareholding csv (P15-04): header exactly <c>symbol,period_end,category,pct,pledged_pct,broadcast_ts</c>; categories <c>promoter</c>, <c>fii</c>, <c>dii</c>, <c>public</c>; <c>pct</c> and <c>pledged_pct</c> from 0 to 100 with up to 4 decimals, blank = unavailable;
/// <c>pledged_pct</c> only on the promoter row. A value outside 0 to 100 is <c>out_of_range</c>; anything else that does not fit is <c>bad_row</c>.
/// </summary>
public static class ShareholdingCsv
{
    public static readonly string[] Header = ["symbol", "period_end", "category", "pct", "pledged_pct", "broadcast_ts"];

    public static readonly string[] Categories = ["promoter", "fii", "dii", "public"];

    public static ShareholdingParse Parse(string text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(text);
        var records = CsvTable.Parse(text);
        if (records.Count == 0 || records[0].Fields is not { } header || !header.SequenceEqual(Header, StringComparer.Ordinal))
        {
            return new ShareholdingParse(CalendarRejectReasons.BadHeader, [], []);
        }

        if (records.Count - 1 > CsvCells.MaxRows)
        {
            return new ShareholdingParse(CalendarRejectReasons.TooManyRows, [], []);
        }

        var rows = new List<ShareholdingRow>();
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
            var category = f[2].Trim();
            var pct = CsvCells.Pct(f[3]);
            var pledged = CsvCells.Pct(f[4]);
            var broadcast = BroadcastRule.Evaluate(f[5], now);
            string? badColumn = symbol is null ? Header[0] : period is null ? Header[1] : !Categories.Contains(category, StringComparer.Ordinal) ? Header[2] : pct.Kind == PctKind.BadRow ? Header[3] : pledged.Kind == PctKind.BadRow ? Header[4] : broadcast.Reason == BroadcastRule.InFuture ? Header[5] : null;
            if (badColumn is null && pledged.Kind != PctKind.Blank && category != "promoter")
            {
                badColumn = Header[4];
            }

            if (badColumn is not null)
            {
                rejects.Add(new RowReject(record.Line, CalendarRejectReasons.BadRow, badColumn));
                continue;
            }

            if (pct.Kind == PctKind.OutOfRange || pledged.Kind == PctKind.OutOfRange)
            {
                rejects.Add(new RowReject(record.Line, CalendarRejectReasons.OutOfRange, pct.Kind == PctKind.OutOfRange ? Header[3] : Header[4]));
                continue;
            }

            rows.Add(new ShareholdingRow(
                record.Line,
                symbol!,
                period!.Value,
                category,
                pct.Kind == PctKind.Value ? pct.Value : null,
                pledged.Kind == PctKind.Value ? pledged.Value : null,
                broadcast));
        }

        return new ShareholdingParse(null, rows, rejects);
    }
}
