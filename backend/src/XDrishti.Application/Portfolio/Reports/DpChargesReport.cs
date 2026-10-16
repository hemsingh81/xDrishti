using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>Which side of the market a depository line is for: a sale (the depository charges on sells), a purchase, or neither said.</summary>
internal enum DpSide
{
    Other,
    Buy,
    Sell,
}

/// <summary>A line of the depository-charges sheet: the day, the security (ISIN and name), the quantity, the side, the type of transaction (<c>Other Fees</c>, <c>DP Fees</c>, <c>Off-Market Fees</c>) and the charge in rupees (0 or above).</summary>
internal sealed record DpLine(int Line, DateOnly Date, string Isin, string Name, long Quantity, DpSide Side, string Type, decimal Charge);

/// <summary>What the reader made of a depository-charges sheet: its lines, the bad lines, the client code (only to be compared, never stored) and the period of its title.</summary>
internal sealed record DpFile(int Rows, IReadOnlyList<DpLine> Lines, IReadOnlyList<TradeBadRow> Bad, int BadTotal, string? ClientCode, DateOnly? From, DateOnly? To);

/// <summary>
/// Reads Dhan's depository-charges sheet (ADR 0029; <c>.xls</c>, <c>.xlsx</c> or CSV) from its rows: a title (<c>DP Charges | From 01-April-2026 to 15-October-2026</c>), personal cells (only the client code is read, for a comparison), the header
/// (<c>Sr., Date, ISIN, Security Name, …, Quantity, Buy/Sell, Type of Transaction, …, Charges</c>), the lines — text cells may have leading spaces — and a <c>Total</c> row, a system-generated note and a <c>Generated on</c> row, which have no date and are ignored. A line
/// with a date needs an ISIN, a quantity that is a whole number (0 and empty are 0) and a charge of 0 or more (empty is 0); the first problem makes it a bad line. Pure.
/// </summary>
internal static class DpChargesReader
{
    private const int MaxTypeLength = 60;

    public static Result<DpFile> Read(Sheet sheet, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var headerAt = HeaderMap.FindHeaderRow(sheet, "date", "isin", "securityname", "typeoftransaction", "charges");
        if (headerAt < 0)
        {
            return Result.Failure<DpFile>(PortfolioErrors.Invalid("file", "The depository-charges sheet needs the columns Date, ISIN, Security Name, Type of Transaction and Charges in its header."));
        }

        var header = new HeaderMap(sheet.Rows[headerAt].Cells);
        var date = header.Find("date");
        var isin = header.Find("isin");
        var name = header.Find("securityname");
        var quantity = header.Find("quantity", "qty");
        var side = header.Find("buysell", "side");
        var type = header.Find("typeoftransaction");
        var charge = header.Find("charges");
        var before = sheet.Rows.Take(headerAt).Where(r => !r.IsBlank).ToList();
        var (from, to) = ReportText.Period(before.FirstOrDefault());
        var lines = new List<DpLine>();
        var bad = new List<TradeBadRow>();
        var badTotal = 0;
        var rows = 0;
        foreach (var row in sheet.Rows.Skip(headerAt + 1))
        {
            if (row.IsBlank || string.IsNullOrWhiteSpace(row.At(date)))
            {
                // The Total row, the system-generated note and the Generated on row have no date.
                continue;
            }

            rows++;
            var read = ReadLine(row, (date, isin, name, quantity, side, type, charge), today, out var problem);
            if (read is null)
            {
                badTotal++;
                if (bad.Count < TradeCsvLimits.MaxListedBad)
                {
                    bad.Add(new TradeBadRow(row.Line, problem!));
                }

                continue;
            }

            lines.Add(read);
        }

        return Result.Success(new DpFile(rows, lines, bad, badTotal, ReportText.ClientCode(before), from, to));
    }

    private static DpLine? ReadLine(SheetRow row, (int Date, int Isin, int Name, int Quantity, int Side, int Type, int Charge) at, DateOnly today, out string? problem)
    {
        problem = null;
        var dateText = row.At(at.Date);
        if (ReportCells.TryParseDate(dateText, out var day) is { } dateProblem)
        {
            problem = $"Date {ReportCells.Quote(dateText)} {dateProblem}";
            return null;
        }

        if (day > today || day < TradeRules.EarliestDate)
        {
            problem = $"Date {ReportCells.Quote(dateText)}: must be between {TradeRules.EarliestDate:yyyy-MM-dd} and today.";
            return null;
        }

        var code = TradeRules.NormaliseIsin(row.At(at.Isin));
        if (code is null || !TradeCells.IsIsin(code))
        {
            problem = $"ISIN {ReportCells.Quote(row.At(at.Isin))} is not an ISIN (12 characters, for example INE000A01011).";
            return null;
        }

        long qty = 0;
        var qtyText = row.At(at.Quantity);
        if (!string.IsNullOrWhiteSpace(qtyText) && ReportCells.TryParseWhole(qtyText, out qty) is { } qtyProblem)
        {
            problem = $"Quantity {ReportCells.Quote(qtyText)} {qtyProblem}";
            return null;
        }

        if (qty is < 0 or > TradeRules.MaxQty)
        {
            problem = "Quantity is out of range.";
            return null;
        }

        var paid = 0m;
        var chargeText = row.At(at.Charge);
        if (!string.IsNullOrWhiteSpace(chargeText) && ReportCells.TryParseNumber(chargeText, out paid) is { } chargeProblem)
        {
            problem = $"Charge {ReportCells.Quote(chargeText)} {chargeProblem}";
            return null;
        }

        if (paid < 0m || paid > TradeRules.MaxCharges)
        {
            problem = $"Charge {ReportCells.Quote(chargeText)}: must be between 0 and {TradeRules.MaxCharges:N0}.";
            return null;
        }

        return new DpLine(
            row.Line,
            day,
            code,
            ReportText.Clean(row.At(at.Name)),
            qty,
            Side(row.At(at.Side)),
            ReportText.Clean(row.At(at.Type), MaxTypeLength),
            Math.Round(paid, 2, MidpointRounding.AwayFromZero));
    }

    private static DpSide Side(string text) => TradeRules.TryParseSide(text, out var side) ? (side == TradeSide.Buy ? DpSide.Buy : DpSide.Sell) : DpSide.Other;
}
