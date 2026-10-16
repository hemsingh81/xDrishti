using System.Globalization;
using System.Text.RegularExpressions;
using XDrishti.Application.Portfolio.Trades;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// Reads the date cell of a dividend row (ADR 0028): <c>13 Jun 2024</c>, <c>23 Sept 2024</c> (Dhan writes Sept for September), <c>13 June 2024</c>, <c>2024-06-13</c>, <c>13-06-2024</c>, <c>13/06/2024</c>, <c>13-Jun-2024</c> — day first, never month first. A time
/// after the date is ignored. Numbers are read by <see cref="TradeCells"/> (<c>.</c> decimal point, <c>,</c> groups thousands). Pure; says why it refused.
/// </summary>
internal static partial class DividendCells
{
    private static readonly string[] DateFormats = ["d MMM yyyy", "d MMMM yyyy", "d-MMM-yyyy", "d-MMMM-yyyy", "yyyy-M-d", "d-M-yyyy", "d/M/yyyy", "d MMM, yyyy"];

    public static string? TryParseDate(string text, out DateOnly date)
    {
        date = default;
        var value = text.Trim();
        if (value.Length == 0)
        {
            return "is empty.";
        }

        if (value.Length > TradeCells.MaxCellLength)
        {
            return "is too long to be a date.";
        }

        var plain = Sept().Replace(value, "Sep");
        if (DateOnly.TryParseExact(plain, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return null;
        }

        // A date with a time after it (an export of a spreadsheet): the day is what counts.
        return TradeCells.TryParseDate(plain, out date, out _) is null
            ? null
            : "is not a date; use dd MMM yyyy (13 Jun 2024), yyyy-MM-dd, dd-MM-yyyy or dd/MM/yyyy (day first).";
    }

    [GeneratedRegex(@"(?<![A-Za-z])sept(?![A-Za-z])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex Sept();
}
