using System.Globalization;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The span of a context period (P15-03). A duration of 80 to 100 days (inclusive of both ends) that ends on the envelope's period end is a quarter; one that starts on 1 April and is longer than 100 days is year to date, or the full year at 355 to 375 days. An instant, an
/// unreadable date, a period that ends on another day and any other duration have no span (null): the fact is skipped and counted.
/// </summary>
public static class SpanRule
{
    public static string? Classify(string? start, string? end, string? instant, DateOnly periodEnd)
    {
        if (instant is not null || !TryDate(start, out var from) || !TryDate(end, out var to) || to != periodEnd || to < from)
        {
            return null;
        }

        var days = to.DayNumber - from.DayNumber + 1;
        if (days is >= 80 and <= 100)
        {
            return ResultCodes.Quarter;
        }

        if (from.Month == 4 && from.Day == 1 && days > 100)
        {
            return days is >= 355 and <= 375 ? ResultCodes.FullYear : ResultCodes.YearToDate;
        }

        return null;
    }

    private static bool TryDate(string? text, out DateOnly date)
    {
        date = default;
        return text is not null && DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
