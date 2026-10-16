using System.Text;
using System.Text.RegularExpressions;
using XDrishti.Application.Portfolio.Imports;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Reads a broker tradebook (CSV) into trades (ADR 0023). Pure: it reads the stream it is given, line by line, and decides everything from the text; it knows no account, no store and no clock (the caller says what today is). UTF-8 with or without a byte
/// order mark, comma separated, fields in double quotes when they contain commas or line breaks. The header names are matched by <see cref="TradeColumns"/>. Per row, in this order: empty rows are ignored; rows that are not NSE equity trades are skipped with a
/// reason (<see cref="TradeSkips"/>); then the date (day first, never month first, not in the future, India time), side, whole quantity above 0, price above 0, charges (the total column, else the components summed) and the optional ISIN and trade id are
/// checked — the first problem makes the row a bad row (line and reason), which is never imported. A trade id that appears again with identical details is a repeat (counted, dropped); with different details it is a bad row. At most
/// <see cref="TradeCsvLimits.MaxRows"/> data rows are read. No row content is ever logged.
/// </summary>
internal static class TradeCsvReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    public static async ValueTask<Result<TradeFile>> ReadAsync(Stream content, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.CanSeek && content.Length > TradeCsvLimits.MaxBytes)
        {
            return Result.Failure<TradeFile>(TooLarge());
        }

        try
        {
            using var reader = new StreamReader(content, StrictUtf8, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
            return await ReadRowsAsync(reader, today, cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            return Result.Failure<TradeFile>(PortfolioErrors.Invalid("file", "The file is not valid UTF-8 text. Save the tradebook as CSV (UTF-8) and try again."));
        }
    }

    private static async ValueTask<Result<TradeFile>> ReadRowsAsync(StreamReader reader, DateOnly today, CancellationToken cancellationToken)
    {
        var lines = new LineReader(reader);
        TradeColumns? columns = null;
        var rows = 0;
        var chars = 0L;
        var trades = new List<ParsedTrade>();
        var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = 0;
        var skippedByReason = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bad = new List<TradeBadRow>();
        var badTotal = 0;
        while (await lines.NextAsync(cancellationToken) is { } record)
        {
            chars += record.Length;
            if (chars > TradeCsvLimits.MaxBytes)
            {
                return Result.Failure<TradeFile>(TooLarge());
            }

            if (record.Fields.All(f => f.Trim().Length == 0))
            {
                continue;
            }

            if (columns is null)
            {
                var found = TradeColumns.Find(record.Fields);
                if (found.IsFailure)
                {
                    return Result.Failure<TradeFile>(Redirect(record.Fields) ?? found.Error);
                }

                columns = found.Value;
                continue;
            }

            if (++rows > TradeCsvLimits.MaxRows)
            {
                return Result.Failure<TradeFile>(PortfolioErrors.Invalid("file", $"The file has more than {TradeCsvLimits.MaxRows:N0} data rows. Split it by date and import the parts one after the other."));
            }

            ParsedTrade? row;
            string? problem;
            try
            {
                var skip = SkipReason(columns, record.Fields);
                if (skip is not null)
                {
                    skippedByReason[skip] = skippedByReason.GetValueOrDefault(skip) + 1;
                    continue;
                }

                row = ReadRow(columns, record, today, out problem);
            }
            catch (RegexMatchTimeoutException)
            {
                // A value built to make a pattern slow (or just absurdly long): the row is bad, the file goes on.
                row = null;
                problem = "A value of the row is too long to be read.";
            }

            if (row is not null && row.TradeId is { } tradeId)
            {
                // Exchanges number trades per day: the id is the identity together with the trade date.
                var id = TradeIdentity.ForTradeId(row.Date, tradeId);
                if (seenIds.TryGetValue(id, out var first))
                {
                    if (first == row.ContentKey)
                    {
                        duplicates++;
                        continue;
                    }

                    row = null;
                    problem = $"Trade id {TradeCells.Quote(tradeId)} appears again on the same day with different details.";
                }
                else
                {
                    seenIds[id] = row.ContentKey;
                }
            }

            if (row is null)
            {
                badTotal++;
                if (bad.Count < TradeCsvLimits.MaxListedBad)
                {
                    bad.Add(new TradeBadRow(record.Line, problem!));
                }

                continue;
            }

            trades.Add(row);
        }

        if (columns is null)
        {
            return Result.Failure<TradeFile>(PortfolioErrors.Invalid("file", "The file is empty: the first line must be the header. Download the template for the layout."));
        }

        return Result.Success(new TradeFile(rows, trades, duplicates, skippedByReason.Values.Sum(), skippedByReason, bad, badTotal));
    }

    /// <summary>
    /// The refusal for a file that is not a tradebook but is recognisably something else (ADR 0028, 0029): a dividend list is sent to the dividend import, a tradebook without a buy/sell column is explained, one of Dhan's downloaded reports (known by its title line) is sent to the report import. Null for any other header: the missing columns are then named.
    /// </summary>
    private static Error? Redirect(IReadOnlyList<string> header) => FileKindDetector.DetectTitle(header) is var titled && FileKindDetector.IsReport(titled)
        ? PortfolioErrors.WrongFileKind(FileKindDetector.Code(titled), FileKindDetector.ReportElsewhereText(titled))
        : FileKindDetector.Detect(header) switch
        {
            FileKind.Dividends => PortfolioErrors.WrongFileKind(FileKindDetector.DividendsCode, FileKindDetector.DividendsInTradesText),
            FileKind.TradebookWithoutSides => PortfolioErrors.WrongFileKind(FileKindDetector.TradebookWithoutSidesCode, FileKindDetector.TradebookWithoutSidesText),
            _ => null,
        };

    /// <summary>Why the row is not an NSE equity trade; null when it is one (or at least not recognisably something else).</summary>
    private static string? SkipReason(TradeColumns columns, IReadOnlyList<string> cells)
    {
        var symbol = Cell(cells, columns.Symbol).Trim().ToUpperInvariant();
        if (symbol.StartsWith("BSE:", StringComparison.Ordinal))
        {
            return TradeSkips.Bse;
        }

        var normal = TradeRules.NormaliseSymbol(symbol);
        if (TradeSkips.LooksLikeDerivative(normal))
        {
            return TradeSkips.Derivatives;
        }

        return TradeSkips.ClassifyCell(Cell(cells, columns.Exchange)) ?? TradeSkips.ClassifyCell(Cell(cells, columns.Segment));
    }

    private static ParsedTrade? ReadRow(TradeColumns columns, CsvRecord record, DateOnly today, out string? problem)
    {
        problem = null;
        var cells = record.Fields;
        var symbol = TradeRules.NormaliseSymbol(Cell(cells, columns.Symbol));
        var isin = TradeRules.NormaliseIsin(Cell(cells, columns.Isin));
        if (symbol.Length == 0 && isin is null)
        {
            problem = "The symbol and the ISIN are both empty.";
            return null;
        }

        if (symbol.Length > Holding.MaxSymbolLength)
        {
            problem = $"Symbol is longer than {Holding.MaxSymbolLength} characters.";
            return null;
        }

        if (isin is not null && !TradeCells.IsIsin(isin))
        {
            problem = $"ISIN {TradeCells.Quote(isin)} is not an ISIN (12 characters, for example INE000A01011).";
            return null;
        }

        var sideText = Cell(cells, columns.Side);
        if (!TradeRules.TryParseSide(sideText, out var side))
        {
            problem = $"Side {TradeCells.Quote(sideText)} is not buy or sell.";
            return null;
        }

        var dateText = Cell(cells, columns.Date);
        if (TradeCells.TryParseDate(dateText, out var date, out var time) is { } dateProblem)
        {
            problem = $"Date {TradeCells.Quote(dateText)} {dateProblem}";
            return null;
        }

        var dateErrors = new FieldErrors();
        TradeRules.CheckDate(dateErrors, "date", date, today);
        if (dateErrors.HasErrors)
        {
            problem = $"Date {TradeCells.Quote(dateText)}: {dateErrors.Items[0].Message}";
            return null;
        }

        if (columns.Time >= 0 && Cell(cells, columns.Time).Trim().Length > 0)
        {
            var timeText = Cell(cells, columns.Time);
            if (TradeCells.TryParseTime(timeText, out var columnTime) is { } timeProblem)
            {
                problem = $"Time {TradeCells.Quote(timeText)} {timeProblem}";
                return null;
            }

            time = columnTime ?? time;
        }

        var qtyText = Cell(cells, columns.Qty);
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

        var priceText = Cell(cells, columns.Price);
        if (TradeCells.TryParseNumber(priceText, out var price) is { } priceProblem)
        {
            problem = $"Price {TradeCells.Quote(priceText)} {priceProblem}";
            return null;
        }

        price = TradeRules.Round(price);
        if (TradeRules.PriceProblem(price) is { } priceRule)
        {
            problem = $"Price {TradeCells.Quote(priceText)}: {priceRule}";
            return null;
        }

        var charges = Charges(columns, cells, out problem);
        if (charges is null)
        {
            return null;
        }

        var tradeId = Cell(cells, columns.TradeId).Trim();
        if (tradeId.Length > TradeCsvLimits.MaxTradeIdLength)
        {
            problem = $"Trade id is longer than {TradeCsvLimits.MaxTradeIdLength} characters.";
            return null;
        }

        var orderId = Cell(cells, columns.OrderId).Trim();
        var exchange = Cell(cells, columns.Exchange).Trim().ToUpperInvariant();
        return new ParsedTrade(
            record.Line,
            symbol,
            isin,
            exchange.Length == 0 || exchange.Contains("NSE", StringComparison.Ordinal) ? "NSE" : exchange,
            side,
            date,
            time,
            qty,
            price,
            charges.Value,
            tradeId.Length == 0 ? null : tradeId,
            orderId.Length == 0 || orderId.Length > TradeCsvLimits.MaxTradeIdLength ? null : orderId);
    }

    /// <summary>The charges of the row: the total column as it is, else the components summed (an empty cell counts as 0); null (and a problem) for a value that is not a number or is negative.</summary>
    private static decimal? Charges(TradeColumns columns, IReadOnlyList<string> cells, out string? problem)
    {
        problem = null;
        var total = 0m;
        IEnumerable<int> indexes = columns.ChargesTotal >= 0 ? new[] { columns.ChargesTotal } : columns.ChargeParts;
        foreach (var index in indexes)
        {
            var text = Cell(cells, index);
            if (text.Trim().Length == 0)
            {
                continue;
            }

            if (TradeCells.TryParseNumber(text, out var amount) is { } numberProblem)
            {
                problem = $"Charges {TradeCells.Quote(text)} {numberProblem}";
                return null;
            }

            if (amount < 0m)
            {
                problem = $"Charges {TradeCells.Quote(text)}: cannot be negative.";
                return null;
            }

            total += amount;
        }

        total = TradeRules.Round(total);
        if (TradeRules.ChargesProblem(total) is { } rule)
        {
            problem = $"Charges: {rule}";
            return null;
        }

        return total;
    }

    private static string Cell(IReadOnlyList<string> cells, int index) => index >= 0 && index < cells.Count ? cells[index] : string.Empty;

    private static Error TooLarge() => PortfolioErrors.Invalid("file", $"The file is larger than {TradeCsvLimits.MaxBytes / (1024 * 1024)} MB. Split it by date and import the parts one after the other.");
}
