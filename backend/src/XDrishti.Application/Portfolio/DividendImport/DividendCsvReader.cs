using System.Text;
using System.Text.RegularExpressions;
using XDrishti.Application.Portfolio.Imports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// Reads a dividend payout file (CSV) into dividends (ADR 0028). Pure: it reads the stream it is given, line by line, and decides everything from the text; it knows no account, no store and no clock (the caller says what today is). UTF-8 with or
/// without a byte order mark, comma separated, fields in double quotes when they contain commas (<c>"1,134.00"</c>). The header names are matched by <see cref="DividendColumns"/>; a header that is a tradebook's or a statement of amounts is refused first, whatever else it has (<c>portfolio.wrong_file_kind</c>), and a header with nothing known
/// (or a plain amount that nothing says is a dividend) with the missing columns named (<c>portfolio.invalid</c>). Per row, in this order: empty rows are ignored; then the scrip name (not empty), the date (<see cref="DividendCells"/>, India date, not in the future), the amount paid
/// (above 0, to the paisa), the optional dividend per share (0 or more), quantity (a whole number above 0), ISIN and symbol are checked — the first problem makes the row a bad row (line and reason), which is never imported. Rows that are identical
/// to an earlier row (same date, name, per share, quantity and amount) are counted once; the repeats are listed. At most <see cref="DividendCsvLimits.MaxRows"/> data rows are read. No row content is ever logged.
/// </summary>
internal static class DividendCsvReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    public static async ValueTask<Result<DividendFile>> ReadAsync(Stream content, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.CanSeek && content.Length > DividendCsvLimits.MaxBytes)
        {
            return Result.Failure<DividendFile>(TooLarge());
        }

        try
        {
            using var reader = new StreamReader(content, StrictUtf8, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
            return await ReadRowsAsync(reader, today, cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            return Result.Failure<DividendFile>(PortfolioErrors.Invalid("file", "The file is not valid UTF-8 text. Save the dividend list as CSV (UTF-8) and try again."));
        }
    }

    private static async ValueTask<Result<DividendFile>> ReadRowsAsync(StreamReader reader, DateOnly today, CancellationToken cancellationToken)
    {
        var lines = new LineReader(reader);
        DividendColumns? columns = null;
        var rows = 0;
        var chars = 0L;
        var items = new List<ParsedDividend>();
        var seen = new Dictionary<string, RepeatBuilder>(StringComparer.Ordinal);
        var repeated = 0;
        var parsedAmount = 0m;
        var bad = new List<DividendBadRow>();
        var badTotal = 0;
        while (await lines.NextAsync(cancellationToken) is { } record)
        {
            chars += record.Length;
            if (chars > DividendCsvLimits.MaxBytes)
            {
                return Result.Failure<DividendFile>(TooLarge());
            }

            if (record.Fields.All(f => f.Trim().Length == 0))
            {
                continue;
            }

            if (columns is null)
            {
                var found = Header(record.Fields);
                if (found.IsFailure)
                {
                    return Result.Failure<DividendFile>(found.Error);
                }

                columns = found.Value;
                continue;
            }

            if (++rows > DividendCsvLimits.MaxRows)
            {
                return Result.Failure<DividendFile>(PortfolioErrors.Invalid("file", $"The file has more than {DividendCsvLimits.MaxRows:N0} data rows. Split it by date and import the parts one after the other."));
            }

            ParsedDividend? row;
            string? problem;
            try
            {
                row = ReadRow(columns, record, today, out problem);
            }
            catch (RegexMatchTimeoutException)
            {
                // A value built to make a pattern slow (or just absurdly long): the row is bad, the file goes on.
                row = null;
                problem = "A value of the row is too long to be read.";
            }

            if (row is null)
            {
                badTotal++;
                if (bad.Count < DividendCsvLimits.MaxListedBad)
                {
                    bad.Add(new DividendBadRow(record.Line, problem!));
                }

                continue;
            }

            parsedAmount += row.Amount;
            if (seen.TryGetValue(row.ContentKey, out var first))
            {
                repeated++;
                first.Add(record.Line);
                continue;
            }

            seen[row.ContentKey] = new RepeatBuilder(row);
            items.Add(row);
        }

        if (columns is null)
        {
            return Result.Failure<DividendFile>(PortfolioErrors.Invalid("file", "The file is empty: the first line must be the header (Date, Scrip Name, Dividend Per Share, Quantity, Dividend Paid)."));
        }

        var repeats = seen.Values.Where(b => b.Occurrences > 1).OrderBy(b => b.Row.Line).Take(DividendCsvLimits.MaxListedRepeats).Select(b => b.ToRepeat()).ToList();
        return Result.Success(new DividendFile(rows, items, repeated, repeats, parsedAmount, bad, badTotal));
    }

    /// <summary>
    /// The columns of a dividend file. The kind of file is judged first, from the header alone: a tradebook (with or without a buy/sell column) or a statement of amounts is refused as <c>portfolio.wrong_file_kind</c> whatever columns it has that look like a
    /// dividend's (an amount, a rate); only then are the columns looked for, and an unknown file is refused with the missing columns named.
    /// </summary>
    private static Result<DividendColumns> Header(IReadOnlyList<string> header) => FileKindDetector.Detect(header) switch
    {
        FileKind.Trades => Result.Failure<DividendColumns>(PortfolioErrors.WrongFileKind(
            FileKindDetector.TradesCode, "This looks like a tradebook (it has a buy/sell column), not a dividend list. Import it as trades on the Portfolio screen.")),
        FileKind.TradebookWithoutSides => Result.Failure<DividendColumns>(PortfolioErrors.WrongFileKind(
            FileKindDetector.TradebookWithoutSidesCode, FileKindDetector.TradebookWithoutSidesText)),
        FileKind.Statement => Result.Failure<DividendColumns>(PortfolioErrors.WrongFileKind(FileKindDetector.StatementCode, FileKindDetector.StatementText)),
        _ => DividendColumns.Find(header),
    };

    private static ParsedDividend? ReadRow(DividendColumns columns, CsvRecord record, DateOnly today, out string? problem)
    {
        problem = null;
        var cells = record.Fields;
        var isin = TradeRules.NormaliseIsin(Cell(cells, columns.Isin));
        if (isin is not null && !TradeCells.IsIsin(isin))
        {
            problem = $"ISIN {TradeCells.Quote(isin)} is not an ISIN (12 characters, for example INE000A01011).";
            return null;
        }

        var symbol = TradeRules.NormaliseSymbol(Cell(cells, columns.Symbol));
        if (symbol.Length > Holding.MaxSymbolLength)
        {
            problem = $"Symbol is longer than {Holding.MaxSymbolLength} characters.";
            return null;
        }

        var name = DividendRules.CleanScripName(Cell(cells, columns.Name));
        if (Cell(cells, columns.Name).Trim().Length > DividendRules.MaxScripNameLength)
        {
            problem = $"The scrip name is longer than {DividendRules.MaxScripNameLength} characters.";
            return null;
        }

        name ??= symbol.Length > 0 ? symbol : isin;
        if (name is null)
        {
            problem = "The scrip name is empty.";
            return null;
        }

        var nameKey = DividendNames.Key(name);
        if (nameKey.Length == 0)
        {
            problem = "The scrip name has no letters or digits.";
            return null;
        }

        var dateText = Cell(cells, columns.Date);
        if (DividendCells.TryParseDate(dateText, out var date) is { } dateProblem)
        {
            problem = $"Date {TradeCells.Quote(dateText)} {dateProblem}";
            return null;
        }

        var dateErrors = new FieldErrors();
        DividendRules.CheckPayDate(dateErrors, "date", date, today);
        if (dateErrors.HasErrors)
        {
            problem = $"Date {TradeCells.Quote(dateText)}: {dateErrors.Items[0].Message}";
            return null;
        }

        var amountText = Cell(cells, columns.Amount);
        if (TradeCells.TryParseNumber(amountText, out var amount) is { } amountProblem)
        {
            problem = $"Dividend paid {TradeCells.Quote(amountText)} {amountProblem}";
            return null;
        }

        amount = DividendRules.RoundAmount(amount);
        if (DividendRules.AmountProblem(amount) is { } amountRule)
        {
            problem = $"Dividend paid {TradeCells.Quote(amountText)}: {amountRule}";
            return null;
        }

        decimal? perShare = null;
        var shareText = Cell(cells, columns.PerShare);
        if (shareText.Trim().Length > 0)
        {
            if (TradeCells.TryParseNumber(shareText, out var share) is { } shareProblem)
            {
                problem = $"Dividend per share {TradeCells.Quote(shareText)} {shareProblem}";
                return null;
            }

            share = DividendRules.RoundPerShare(share);
            if (share < 0m || share > DividendRules.MaxPerShare)
            {
                problem = $"Dividend per share {TradeCells.Quote(shareText)}: must be between 0 and {DividendRules.MaxPerShare:N0}.";
                return null;
            }

            perShare = share;
        }

        long? quantity = null;
        var qtyText = Cell(cells, columns.Qty);
        if (qtyText.Trim().Length > 0)
        {
            if (TradeCells.TryParseWhole(qtyText, out var qty) is { } qtyProblem)
            {
                problem = $"Quantity {TradeCells.Quote(qtyText)} {qtyProblem}";
                return null;
            }

            if (TradeRules.QtyProblem(qty) is { } qtyRule)
            {
                problem = $"Quantity {TradeCells.Quote(qtyText)}: {qtyRule}";
                return null;
            }

            quantity = qty;
        }

        return new ParsedDividend(record.Line, date, name, nameKey, isin, symbol.Length == 0 ? null : symbol, perShare, quantity, amount);
    }

    private static string Cell(IReadOnlyList<string> cells, int index) => index >= 0 && index < cells.Count ? cells[index] : string.Empty;

    private static Error TooLarge() => PortfolioErrors.Invalid("file", $"The file is larger than {DividendCsvLimits.MaxBytes / (1024 * 1024)} MB. Split it by date and import the parts one after the other.");

    /// <summary>The first row of a payment and the lines of the rows that repeat it.</summary>
    private sealed class RepeatBuilder(ParsedDividend row)
    {
        private readonly List<int> _lines = [row.Line];

        public ParsedDividend Row { get; } = row;

        public int Occurrences => _lines.Count;

        public void Add(int line) => _lines.Add(line);

        public DividendRepeat ToRepeat() => new(Row.Name, Row.PayDate, Row.Amount, _lines.Count, Row.Line, [.. _lines.Take(DividendCsvLimits.MaxLinesPerRepeat)]);
    }
}
