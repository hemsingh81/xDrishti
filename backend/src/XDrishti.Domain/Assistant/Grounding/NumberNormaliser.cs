using System.Globalization;

namespace XDrishti.Domain.Assistant.Grounding;

/// <summary>
/// Turns the pieces of a printed figure into a <see cref="ParsedNumber"/> (spec P9-03 AC 6 (i)): Western and Indian digit grouping, a decimal point, an exponent, and the suffixes percent, k (thousand), L or lakh (100,000) and Cr or crore (10,000,000). Pure and total: a text that is not a number gives null.
/// </summary>
public static class NumberNormaliser
{
    private const int MaxDigits = 28;

    /// <summary>Parses <paramref name="digits"/> (digits with optional grouping commas, a point and an exponent) with the unit <paramref name="suffix"/> (empty, "%", "k", "L", "Cr", "R", ...).</summary>
    public static ParsedNumber? Parse(string digits, string suffix)
    {
        ArgumentNullException.ThrowIfNull(digits);
        ArgumentNullException.ThrowIfNull(suffix);
        var text = digits.Replace(",", string.Empty, StringComparison.Ordinal);
        var mantissa = text;
        var exponent = 0;
        var e = text.IndexOfAny(['e', 'E']);
        if (e >= 0)
        {
            mantissa = text[..e];
            if (!int.TryParse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent) || Math.Abs(exponent) > 20)
            {
                return null;
            }
        }

        if (mantissa.Length is 0 or > MaxDigits || !decimal.TryParse(mantissa, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var decimals = point < 0 ? 0 : mantissa.Length - point - 1;
        var multiplier = Multiplier(suffix);
        try
        {
            var unit = Pow10(-decimals + exponent) * multiplier;
            value = checked(value * Pow10(exponent) * multiplier);
            var percent = suffix.Trim().Equals("%", StringComparison.Ordinal) || suffix.Trim().StartsWith("per", StringComparison.OrdinalIgnoreCase);
            var plainInteger = point < 0 && exponent == 0 && multiplier == 1m && !percent && suffix.Trim().Length == 0;
            return new ParsedNumber(value, unit / 2m, percent, plainInteger);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static decimal Multiplier(string suffix) => suffix.Trim().ToLowerInvariant() switch
    {
        "k" or "thousand" => 1_000m,
        "l" or "lakh" or "lakhs" => 100_000m,
        "cr" or "crore" or "crores" => 10_000_000m,
        _ => 1m,
    };

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < Math.Abs(exponent); i++)
        {
            result = exponent > 0 ? result * 10m : result / 10m;
        }

        return result;
    }
}
