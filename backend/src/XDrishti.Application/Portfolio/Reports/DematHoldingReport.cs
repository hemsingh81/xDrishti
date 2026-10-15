using System.Text.RegularExpressions;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>A security of the demat holding statement: its ISIN and name and the depository's quantity (free, locked in, safe keep and every pledge together), with the closing price and valuation the statement gives.</summary>
internal sealed record DematRow(int Line, string Isin, string Name, decimal Quantity, decimal? ClosingPrice, decimal? Valuation);

/// <summary>What the reader made of a demat holding statement: its rows (one per security), the bad rows, the client code (only to be compared, never stored) and the day it is for.</summary>
internal sealed record DematFile(int Rows, IReadOnlyList<DematRow> Items, IReadOnlyList<TradeBadRow> Bad, int BadTotal, string? ClientCode, DateOnly? AsOf);

/// <summary>
/// Reads Dhan's demat holding statement (ADR 0029) from its rows: a title (<c>Holding summary,For 15-10-2026</c>), personal lines (only the client code is read, for a comparison), the header (<c>Scrip Name, ISIN Code, Free Holding, Locked In, Safe Keep,
/// MTF Pledge, Margin Pledge, CUSA Pledge, Closing Price, Valuation</c>), one row per security and a footer (<c>Valuation, …, Total Number of Securities, …</c>) which is ignored. The depository quantity of a security is the sum of all the quantity columns. A row needs a name,
/// an ISIN and quantities that are numbers of 0 or more; the first problem makes it a bad row. Pure.
/// </summary>
internal static partial class DematHoldingReader
{
    private static readonly string[] QuantityColumns = ["freeholding", "lockedin", "safekeep", "mtfpledge", "marginpledge", "cusapledge"];

    public static Result<DematFile> Read(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var headerAt = HeaderMap.FindHeaderRow(sheet, "scripname", "isincode", "freeholding");
        if (headerAt < 0)
        {
            return Result.Failure<DematFile>(PortfolioErrors.Invalid("file", "The demat holding statement needs the columns Scrip Name, ISIN Code and Free Holding in its header."));
        }

        var header = new HeaderMap(sheet.Rows[headerAt].Cells);
        var name = header.Find("scripname");
        var isin = header.Find("isincode");
        var quantities = QuantityColumns.Select(c => header.Find(c)).Where(i => i >= 0).ToArray();
        var price = header.Find("closingprice");
        var valuation = header.Find("valuation");
        var before = sheet.Rows.Take(headerAt).Where(r => !r.IsBlank).ToList();
        var items = new List<DematRow>();
        var bad = new List<TradeBadRow>();
        var badTotal = 0;
        var rows = 0;
        foreach (var row in sheet.Rows.Skip(headerAt + 1))
        {
            if (row.IsBlank || ReportCells.Norm(row.Cells.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))) == "valuation")
            {
                continue;
            }

            rows++;
            var read = ReadRow(row, name, isin, quantities, price, valuation, out var problem);
            if (read is null)
            {
                badTotal++;
                if (bad.Count < TradeCsvLimits.MaxListedBad)
                {
                    bad.Add(new TradeBadRow(row.Line, problem!));
                }

                continue;
            }

            items.Add(read);
        }

        return Result.Success(new DematFile(rows, items, bad, badTotal, ReportText.ClientCode(before), AsOf(before.FirstOrDefault())));
    }

    private static DematRow? ReadRow(SheetRow row, int name, int isin, int[] quantities, int price, int valuation, out string? problem)
    {
        problem = null;
        var scrip = ReportText.Clean(row.At(name));
        if (scrip.Length == 0)
        {
            problem = "The scrip name is empty.";
            return null;
        }

        var code = TradeRules.NormaliseIsin(row.At(isin));
        if (code is null || !TradeCells.IsIsin(code))
        {
            problem = $"ISIN {ReportCells.Quote(row.At(isin))} is not an ISIN (12 characters, for example INE000A01011).";
            return null;
        }

        var total = 0m;
        foreach (var index in quantities)
        {
            var text = row.At(index);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var partProblem = ReportCells.TryParseNumber(text, out var part);
            if (partProblem is not null || part < 0m)
            {
                problem = $"Quantity {ReportCells.Quote(text)} {partProblem ?? "cannot be negative."}";
                return null;
            }

            total += part;
        }

        if (total > TradeRules.MaxQty)
        {
            problem = "Quantity is out of range.";
            return null;
        }

        return new DematRow(row.Line, code, scrip, total, Optional(row.At(price)), Optional(row.At(valuation)));
    }

    private static decimal? Optional(string text) => !string.IsNullOrWhiteSpace(text) && ReportCells.TryParseNumber(text, out var value) is null ? value : null;

    private static DateOnly? AsOf(SheetRow? title)
    {
        if (title is null)
        {
            return null;
        }

        var match = ForDate().Match(string.Join(' ', title.Cells));
        return match.Success && ReportCells.TryParseDate(match.Groups["date"].Value, out var day) is null ? day : null;
    }

    [GeneratedRegex(@"For\s+(?<date>\d{1,2}[- ][A-Za-z0-9]+[- ]\d{4})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex ForDate();
}
