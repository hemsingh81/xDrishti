using System.Globalization;

namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>
/// The bounds of a proposed parameter move (spec P9-08 AC 4, D3): the candidate has the baseline's sign, differs from it, is within +/- <c>maxMovePct</c> percent of it (50 at most: the largest single step P6-10 allows) and has at most six significant digits. Decimal arithmetic, so
/// 50 percent is exactly allowed and 50.01 is not. A language model can ask for any value; this says which values the engine will look at. Pure.
/// </summary>
public static class KnobBounds
{
    public const int MaxSignificantDigits = 6;
    public const int DefaultMaxMovePct = 50;

    public const string NotNumber = "knob_value_not_number";
    public const string BaselineZero = "knob_baseline_zero";
    public const string SignChange = "knob_sign_change";
    public const string Unchanged = "knob_unchanged";
    public const string MoveTooLarge = "knob_move_too_large";
    public const string TooManyDigits = "knob_too_many_digits";

    /// <summary>The codes that say why the candidate is not allowed; empty when it is.</summary>
    public static IReadOnlyList<string> Problems(string? baseline, string? candidate, int maxMovePct = DefaultMaxMovePct)
    {
        if (!TryParse(baseline, out var b) || !TryParse(candidate, out var c))
        {
            return [NotNumber];
        }

        if (b == 0)
        {
            return [BaselineZero];
        }

        var problems = new List<string>();
        if (c == 0 || (b > 0) != (c > 0))
        {
            problems.Add(SignChange);
        }

        if (c == b)
        {
            problems.Add(Unchanged);
        }

        if (Math.Abs(c - b) * 100m > Math.Abs(b) * Math.Clamp(maxMovePct, 1, DefaultMaxMovePct))
        {
            problems.Add(MoveTooLarge);
        }

        if (SignificantDigits(c) > MaxSignificantDigits)
        {
            problems.Add(TooManyDigits);
        }

        return problems;
    }

    /// <summary>The value as a canonical number text (no trailing zeros), or null when it is not a finite number the engine can handle: <c>2.50</c> and <c>2.5</c> are one value.</summary>
    public static string? Normal(string? text) => TryParse(text, out var v) ? Format(v) : null;

    internal static string Format(decimal value) => (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    internal static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(text) && text.Length <= 40 && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value);
    }

    private static int SignificantDigits(decimal value)
    {
        var digits = Format(Math.Abs(value)).Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('0').TrimEnd('0');
        return digits.Length;
    }
}
