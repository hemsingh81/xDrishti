using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>Rules for acquisition dates and the first import date (ADR 0012, 0013).</summary>
public static class HoldingRules
{
    /// <summary>The first import date of an account whose owner has not chosen one (ADR 0013).</summary>
    public static readonly DateOnly DefaultImportDate = new(2025, 1, 1);

    /// <summary>The earliest acquisition date accepted.</summary>
    public static readonly DateOnly EarliestDate = new(1990, 1, 1);

    /// <summary>Adds a field error unless the date is between <see cref="EarliestDate"/> and <paramref name="today"/> (India).</summary>
    public static void CheckDate(FieldErrors errors, string field, DateOnly date, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (date > today)
        {
            errors.Add(field, "Cannot be in the future.");
        }
        else if (date < EarliestDate)
        {
            errors.Add(field, $"Cannot be before {EarliestDate:yyyy-MM-dd}.");
        }
    }
}
