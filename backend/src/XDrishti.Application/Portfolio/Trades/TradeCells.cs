using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>Reads the cells of a tradebook row: dates, times and numbers in the formats ADR 0023 accepts, and nothing else. Pure; every parser says why it refused.</summary>
internal static partial class TradeCells
{
    /// <summary>No date, time or number is longer than this; a longer cell is refused before any pattern looks at it.</summary>
    public const int MaxCellLength = 64;

    // dd-MM-yyyy and dd/MM/yyyy are day first, MM/dd/yyyy is never guessed: 13/25/2024 is no date. One or two digits stand for the day and month.
    private static readonly string[] DateFormats = ["yyyy-M-d", "d-M-yyyy", "d/M/yyyy", "d-MMM-yyyy"];

    private static readonly string[] TimeFormats = ["H:mm:ss", "H:mm", "H:mm:ss.FFFFFFF", "h:mm:ss tt", "h:mm tt"];

    /// <summary>
    /// A date, optionally followed by a time (<c>2024-03-05T09:15:23</c>, <c>05-03-2024 09:15</c>). Times are India time; a time zone (<c>Z</c>, <c>+05:30</c>) is refused rather than converted.
    /// </summary>
    public static string? TryParseDate(string text, out DateOnly date, out TimeOnly? time)
    {
        date = default;
        time = null;
        var value = text.Trim();
        if (value.Length > MaxCellLength)
        {
            return "is too long to be a date.";
        }

        var split = SplitDateTime(value);
        var datePart = split < 0 ? value : value[..split];
        var timePart = split < 0 ? string.Empty : value[(split + 1)..].Trim();
        if (!DateOnly.TryParseExact(datePart, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return "is not a date; use yyyy-MM-dd, dd-MM-yyyy, dd/MM/yyyy or dd-MMM-yyyy (day first).";
        }

        if (timePart.Length > 0)
        {
            var problem = TryParseTime(timePart, out var parsed);
            if (problem is not null)
            {
                return problem;
            }

            time = parsed;
        }

        return null;
    }

    /// <summary>A time of day, India time: <c>9:15</c>, <c>09:15:23</c>, <c>09:15:23.123</c>, <c>9:15 AM</c>. Also accepts a full date and time and takes its time.</summary>
    public static string? TryParseTime(string text, out TimeOnly? time)
    {
        time = null;
        var value = text.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length > MaxCellLength)
        {
            return "is too long to be a time.";
        }

        value = WithoutDate(value);

        if (value.EndsWith('Z') || ZoneOffset().IsMatch(value))
        {
            return "has a time zone; times must be India time without one.";
        }

        if (!TimeOnly.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return "is not a time; use HH:mm or HH:mm:ss (India time).";
        }

        time = parsed;
        return null;
    }

    /// <summary>A decimal number: <c>.</c> is the decimal point and <c>,</c> groups thousands (<c>1,234.50</c> or the Indian <c>12,34,567.00</c>); a comma anywhere else, an exponent or a currency sign is refused.</summary>
    public static string? TryParseNumber(string text, out decimal value)
    {
        value = 0m;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return "is empty.";
        }

        if (trimmed.Length > MaxCellLength)
        {
            return "is too long to be a number.";
        }

        if (!Number().IsMatch(trimmed) || !decimal.TryParse(trimmed.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            return "is not a number; use a point for decimals and commas only to group thousands.";
        }

        return null;
    }

    /// <summary>A whole number of shares (<c>10</c> or <c>10.00</c>); a fraction is refused.</summary>
    public static string? TryParseWhole(string text, out long value)
    {
        value = 0;
        var problem = TryParseNumber(text, out var number);
        if (problem is not null)
        {
            return problem;
        }

        if (number != decimal.Truncate(number))
        {
            return "must be a whole number of shares.";
        }

        if (number is > long.MaxValue or < long.MinValue)
        {
            return "is too large.";
        }

        value = (long)number;
        return null;
    }

    /// <summary>The time part of a full date and time (<c>2024-03-05T09:15:23</c>, <c>05-03-2024 09:15</c>); a time alone is returned as it is.</summary>
    private static string WithoutDate(string value)
    {
        var split = SplitDateTime(value);
        return split > 0 && DateOnly.TryParseExact(value[..split], DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? value[(split + 1)..].Trim() : value;
    }

    /// <summary>Where the time starts in a date and time: the first space, or the <c>T</c> between digits of an ISO timestamp (not the T of <c>OCT</c>); -1 when there is no time.</summary>
    private static int SplitDateTime(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == ' ' || (value[i] == 'T' && i > 0 && i + 1 < value.Length && char.IsDigit(value[i - 1]) && char.IsDigit(value[i + 1])))
            {
                return i;
            }
        }

        return -1;
    }

    public static bool IsIsin(string text) => text.Length <= MaxCellLength && IsinPattern().IsMatch(text);

    /// <summary>The cell as it may be quoted in a message: control characters gone, at most 30 characters.</summary>
    public static string Quote(string text)
    {
        var clean = new string([.. text.Trim().Where(c => !char.IsControl(c))]);
        return clean.Length <= 30 ? $"\"{clean}\"" : $"\"{clean[..30]}…\"";
    }

    [GeneratedRegex(@"^[+-]?(\d+|\d{1,3}(,\d{3})+|\d{1,2}(,\d{2})+,\d{3})(\.\d+)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Number();

    [GeneratedRegex(@"[+-]\d{2}:?\d{2}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex ZoneOffset();

    [GeneratedRegex(@"^[A-Z]{2}[A-Z0-9]{9}[0-9]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex IsinPattern();
}
