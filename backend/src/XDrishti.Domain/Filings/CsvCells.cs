using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Filings;

/// <summary>The cell rules the two csv formats share (P15-04): a symbol, a date and a percentage. Nothing here guesses: a cell that does not match is rejected.</summary>
internal static partial class CsvCells
{
    public const int MaxRows = 20_000;

    private static readonly DateOnly EarliestDate = new(2000, 1, 1);

    private static readonly DateOnly LatestDate = new(2100, 12, 31);

    [GeneratedRegex("^[A-Z0-9&-]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();

    [GeneratedRegex(@"^[+-]?\d{1,6}(\.\d{1,12})?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    public static string? Symbol(string cell)
    {
        var text = cell.Trim().ToUpperInvariant();
        return SymbolPattern().IsMatch(text) ? text : null;
    }

    public static DateOnly? Date(string cell) =>
        DateOnly.TryParseExact(cell.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date >= EarliestDate && date <= LatestDate ? date : null;

    /// <summary>A percentage cell: <c>Blank</c> when empty, <c>BadRow</c> when it is not a plain number with up to 4 decimals, <c>OutOfRange</c> outside 0 to 100, else the value.</summary>
    public static PctCell Pct(string cell)
    {
        var text = cell.Trim();
        if (text.Length == 0)
        {
            return PctCell.Blank;
        }

        if (!NumberPattern().IsMatch(text))
        {
            return PctCell.BadRow;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0 && text.Length - dot - 1 > 4)
        {
            return PctCell.BadRow;
        }

        var value = decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return value is < 0m or > 100m ? PctCell.OutOfRange : new PctCell(PctKind.Value, value);
    }
}

/// <summary>What a percentage cell turned out to be.</summary>
internal enum PctKind
{
    Blank,
    BadRow,
    OutOfRange,
    Value,
}

/// <summary>A parsed percentage cell.</summary>
internal readonly record struct PctCell(PctKind Kind, decimal Value = 0m)
{
    public static readonly PctCell Blank = new(PctKind.Blank);

    public static readonly PctCell BadRow = new(PctKind.BadRow);

    public static readonly PctCell OutOfRange = new(PctKind.OutOfRange);
}
