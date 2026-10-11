using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>Numeric range checks that report a field error in plain words.</summary>
internal static class Ranges
{
    /// <summary>The value must be greater than <paramref name="exclusiveMin"/> and at most <paramref name="max"/>.</summary>
    public static void GreaterThanUpTo(FieldErrors errors, string field, decimal value, decimal exclusiveMin, decimal max)
    {
        if (value <= exclusiveMin || value > max)
        {
            errors.Add(field, $"Must be greater than {Show(exclusiveMin)} and at most {Show(max)}.");
        }
    }

    /// <summary>The value must be between <paramref name="min"/> and <paramref name="max"/> (both allowed).</summary>
    public static void Between(FieldErrors errors, string field, decimal value, decimal min, decimal max)
    {
        if (value < min || value > max)
        {
            errors.Add(field, $"Must be between {Show(min)} and {Show(max)}.");
        }
    }

    private static string Show(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
