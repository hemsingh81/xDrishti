using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>
/// A row of the global transaction report: the bill's number, day and scrip, and what was bought and sold in it with the sum of the seven charges (brokerage, GST, STT, SEBI fees, stamp duty, transaction charges, other charges; a refund is a negative part).
/// A row can have both a buy and a sell quantity (a same-day round trip).
/// </summary>
/// <remarks>The last member <c>Lines</c> holds the seven charge cells by column as read (spec P3-03f, F5; a blank cell is 0), null for a row built by hand; <c>Charges</c> stays the rounded sum.</remarks>
internal sealed record GtrRow(int Line, DateOnly Date, string Name, string Exchange, string BillNo, long BuyQty, decimal BuyValue, long SellQty, decimal SellValue, decimal Charges, GtrLines? Lines = null);

/// <summary>The seven charge cells of a bill row as the report shows them (a refund is negative, a blank cell is 0), in the report's column order (spec P3-03f, F5). The bill check of the charge sheet reads them line by line.</summary>
internal sealed record GtrLines(decimal Brokerage, decimal Gst, decimal Stt, decimal SebiFees, decimal StampDuty, decimal TxnCharges, decimal OtherCharges);

/// <summary>What the reader made of a global transaction report: the data rows, the rows repeated in the file, the bad rows, the client code (only to be compared, never stored), the period and the report's own total charges.</summary>
internal sealed record GtrFile(
    int Rows,
    IReadOnlyList<GtrRow> Items,
    int Repeated,
    IReadOnlyList<TradeBadRow> Bad,
    int BadTotal,
    string? ClientCode,
    DateOnly? From,
    DateOnly? To,
    decimal? FooterCharges)
{
    /// <summary>The charges of all the rows, whatever becomes of them.</summary>
    public decimal ChargesTotal => Items.Sum(i => i.Charges) + Repeats;

    /// <summary>The charges of rows repeated in the file were counted by the report once more; they are not in <see cref="Items"/>.</summary>
    public decimal Repeats { get; init; }
}

/// <summary>
/// Reads Dhan's global transaction report (ADR 0029) from its rows: a title line (<c>Global transction report,From … to …</c>, sic), personal lines (name, UCC, mobile, e-mail: only the UCC is read, only for a comparison), an empty line, the header
/// (<c>Date, Scrip Name, Exchange, Bill No., Buy Qty., Buy Value, Sell Qty., Sell Value, Brokerage, GST, STT, SEBI Fees, Stamp Duty, Txn. Charges, Oth. Charges, Gross Amount</c>), the rows, a footer (<c>Net P&amp;L … Total Charges</c>) and a note. Dates like
/// <c>07 Apr 2026 00:00:00</c>. Per row the first problem makes it a bad row (line and reason): a date that is not one, in the future or too early, no name or bill number, quantities that are not whole numbers, values that are negative or do not go with
/// the quantity, a price that cannot be kept, charges out of range. A bill that appears again with identical details (same exchange too) is a repeat (counted); with different details a bad row. Pure.
/// </summary>
internal static class GlobalTransactionReader
{
    private static readonly string[] ChargeColumns = ["brokerage", "gst", "stt", "sebifees", "stampduty", "txncharges", "othcharges"];

    public static Result<GtrFile> Read(Sheet sheet, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var headerAt = HeaderMap.FindHeaderRow(sheet, "date", "scripname", "billno", "buyqty", "buyvalue", "sellqty", "sellvalue");
        if (headerAt < 0)
        {
            return Result.Failure<GtrFile>(PortfolioErrors.Invalid("file", "The global transaction report needs the columns Date, Scrip Name, Bill No., Buy Qty., Buy Value, Sell Qty. and Sell Value in its header."));
        }

        var header = new HeaderMap(sheet.Rows[headerAt].Cells);
        var date = header.Find("date");
        var name = header.Find("scripname");
        var exchange = header.Find("exchange");
        var bill = header.Find("billno");
        var buyQty = header.Find("buyqty");
        var buyValue = header.Find("buyvalue");
        var sellQty = header.Find("sellqty");
        var sellValue = header.Find("sellvalue");
        var charges = ChargeColumns.Select(c => header.Find(c)).ToArray();
        if (charges.Any(c => c < 0))
        {
            var missing = ChargeColumns.Where((_, i) => charges[i] < 0);
            return Result.Failure<GtrFile>(PortfolioErrors.Invalid("file", $"The global transaction report needs all seven charge columns (Brokerage, GST, STT, SEBI Fees, Stamp Duty, Txn. Charges, Oth. Charges); missing: {string.Join(", ", missing)}."));
        }

        var before = sheet.Rows.Take(headerAt).Where(r => !r.IsBlank).ToList();
        var (from, to) = ReportText.Period(before.FirstOrDefault());
        var clientCode = ReportText.ClientCode(before);
        var items = new List<GtrRow>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var bad = new List<TradeBadRow>();
        var badTotal = 0;
        var repeated = 0;
        var repeatedCharges = 0m;
        var rows = 0;
        decimal? footer = null;
        foreach (var row in sheet.Rows.Skip(headerAt + 1))
        {
            if (row.IsBlank)
            {
                continue;
            }

            var first = ReportCells.Norm(row.Cells.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)));
            if (first.StartsWith("netpl", StringComparison.Ordinal) || first.StartsWith("totalcharges", StringComparison.Ordinal))
            {
                footer ??= FooterCharges(row);
                continue;
            }

            if (first.StartsWith("note", StringComparison.Ordinal))
            {
                continue;
            }

            rows++;
            var read = ReadRow(row, date, name, exchange, bill, buyQty, buyValue, sellQty, sellValue, charges, today, out var problem);
            if (read is null)
            {
                badTotal++;
                if (bad.Count < TradeCsvLimits.MaxListedBad)
                {
                    bad.Add(new TradeBadRow(row.Line, problem!));
                }

                continue;
            }

            // The exchange is part of the repeat check (an NSE and a BSE bill may share a number); it is not part of the trade's stored identity (BillTrades.Identity).
            var key = $"{read.BillNo}|{read.Date:yyyyMMdd}|{BillName.Parse(read.Name).Key}|{ReportCells.Norm(read.Exchange)}";
            var content = FormattableString.Invariant($"{read.BuyQty}|{read.BuyValue}|{read.SellQty}|{read.SellValue}|{read.Charges}");
            if (seen.TryGetValue(key, out var earlier))
            {
                if (earlier == content)
                {
                    repeated++;
                    repeatedCharges += read.Charges;
                    continue;
                }

                badTotal++;
                if (bad.Count < TradeCsvLimits.MaxListedBad)
                {
                    bad.Add(new TradeBadRow(row.Line, $"Bill {ReportCells.Quote(read.BillNo)} appears again for the same scrip and day with different details."));
                }

                continue;
            }

            seen[key] = content;
            items.Add(read);
        }

        return Result.Success(new GtrFile(rows, items, repeated, bad, badTotal, clientCode, from, to, footer) { Repeats = repeatedCharges });
    }

    private static decimal? FooterCharges(SheetRow row)
    {
        for (var i = 0; i + 1 < row.Cells.Count; i++)
        {
            if (ReportCells.Norm(row.Cells[i]) == "totalcharges" && ReportCells.TryParseNumber(row.Cells[i + 1], out var value) is null)
            {
                return value;
            }
        }

        return null;
    }

    private static GtrRow? ReadRow(
        SheetRow row, int date, int name, int exchange, int bill, int buyQty, int buyValue, int sellQty, int sellValue, int[] charges, DateOnly today, out string? problem)
    {
        problem = null;
        var dateText = row.At(date);
        if (ReportCells.TryParseDate(dateText, out var day) is { } dateProblem)
        {
            problem = $"Date {ReportCells.Quote(dateText)} {dateProblem}";
            return null;
        }

        if (day > today)
        {
            problem = $"Date {ReportCells.Quote(dateText)}: cannot be in the future.";
            return null;
        }

        if (day < TradeRules.EarliestDate)
        {
            problem = $"Date {ReportCells.Quote(dateText)}: cannot be before {TradeRules.EarliestDate:yyyy-MM-dd}.";
            return null;
        }

        var scrip = ReportText.Clean(row.At(name));
        if (scrip.Length == 0)
        {
            problem = "The scrip name is empty.";
            return null;
        }

        var billNo = ReportText.Clean(row.At(bill), 40);
        if (billNo.Length == 0)
        {
            problem = "The bill number is empty.";
            return null;
        }

        if (!Quantity(row.At(buyQty), "Buy quantity", out var bought, out problem) || !Quantity(row.At(sellQty), "Sell quantity", out var sold, out problem)
            || !Amount(row.At(buyValue), "Buy value", out var buy, out problem) || !Amount(row.At(sellValue), "Sell value", out var sell, out problem))
        {
            return null;
        }

        if (bought == 0 && sold == 0)
        {
            problem = "The row has neither a buy nor a sell quantity.";
            return null;
        }

        problem = Leg("Buy", bought, buy) ?? Leg("Sell", sold, sell);
        if (problem is not null)
        {
            return null;
        }

        var total = 0m;
        var cells = new decimal[charges.Length];
        for (var c = 0; c < charges.Length; c++)
        {
            var text = row.At(charges[c]);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (ReportCells.TryParseNumber(text, out var part) is { } chargeProblem)
            {
                problem = $"Charges {ReportCells.Quote(text)} {chargeProblem}";
                return null;
            }

            cells[c] = part;
            total += part;
        }

        total = TradeRules.Round(total);
        if (Math.Abs(total) > TradeRules.MaxCharges)
        {
            problem = $"Charges: at most {TradeRules.MaxCharges:N0}.";
            return null;
        }

        return new GtrRow(row.Line, day, scrip, ReportText.Clean(row.At(exchange), 16).ToUpperInvariant(), billNo, bought, buy, sold, sell, total,
            new GtrLines(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6]));
    }

    /// <summary>The problem with a leg (a side of the bill): a quantity needs a value above 0 whose price can be kept, a value needs a quantity. Null when it is fine.</summary>
    private static string? Leg(string side, long qty, decimal value)
    {
        if (qty == 0)
        {
            return value == 0m ? null : $"{side} value without a {side.ToLowerInvariant()} quantity.";
        }

        if (value <= 0m)
        {
            return $"{side} quantity without a {side.ToLowerInvariant()} value above 0.";
        }

        return TradeRules.QtyProblem(qty) is { } qtyProblem
            ? $"{side} quantity: {qtyProblem}"
            : TradeRules.PriceProblem(TradeRules.RoundPrice(value / qty)) is { } priceProblem ? $"{side} price (value ÷ quantity): {priceProblem}" : null;
    }

    private static bool Quantity(string text, string label, out long value, out string? problem)
    {
        value = 0;
        problem = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (ReportCells.TryParseWhole(text, out value) is { } whole)
        {
            problem = $"{label} {ReportCells.Quote(text)} {whole}";
            return false;
        }

        if (value >= 0)
        {
            return true;
        }

        problem = $"{label} {ReportCells.Quote(text)}: cannot be negative.";
        return false;
    }

    private static bool Amount(string text, string label, out decimal value, out string? problem)
    {
        value = 0m;
        problem = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (ReportCells.TryParseNumber(text, out value) is { } number)
        {
            problem = $"{label} {ReportCells.Quote(text)} {number}";
            return false;
        }

        if (value >= 0m)
        {
            return true;
        }

        problem = $"{label} {ReportCells.Quote(text)}: cannot be negative.";
        return false;
    }
}
