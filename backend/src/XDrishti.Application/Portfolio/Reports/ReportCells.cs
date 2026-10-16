using System.Text.RegularExpressions;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Application.Portfolio.Trades;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>The cells of Dhan's reports (ADR 0029): dates like <c>07 Apr 2026 00:00:00</c>, <c>23 Sept 2026</c>, <c>04-Jun-2026</c>, numbers with thousands separators, and the way header names are compared. Pure; says why it refused.</summary>
internal static partial class ReportCells
{
    /// <summary>A header name reduced to its letters and digits in lower case: <c>Bill No.</c> is <c>billno</c>, <c>Realised P&amp;L %</c> is <c>realisedpl</c>.</summary>
    public static string Norm(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var kept = new char[text.Length];
        var n = 0;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                kept[n++] = char.ToLowerInvariant(c);
            }
        }

        return new string(kept, 0, n);
    }

    /// <summary>A date with or without a time after it (the time is ignored): day first, <c>Sept</c> for September, month names or numbers.</summary>
    public static string? TryParseDate(string text, out DateOnly date)
    {
        date = default;
        var value = text.Trim();
        if (value.Length > TradeCells.MaxCellLength)
        {
            return "is too long to be a date.";
        }

        var withoutTime = TimeSuffix().Replace(value, string.Empty);
        return DividendCells.TryParseDate(withoutTime, out date);
    }

    /// <summary>A number; the empty cell is the problem (callers that allow an empty cell check first).</summary>
    public static string? TryParseNumber(string text, out decimal value) => TradeCells.TryParseNumber(text, out value);

    /// <summary>A whole number of shares or units: <c>650</c>, <c>650.00</c>, <c>11.000</c>.</summary>
    public static string? TryParseWhole(string text, out long value) => TradeCells.TryParseWhole(text, out value);

    /// <summary>The cell as it may be quoted in a message (control characters gone, at most 30 characters).</summary>
    public static string Quote(string text) => TradeCells.Quote(text);

    [GeneratedRegex(@"[ T]\d{1,2}:\d{2}(:\d{2})?(\.\d+)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex TimeSuffix();
}
