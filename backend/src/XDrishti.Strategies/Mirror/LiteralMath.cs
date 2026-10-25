using System.Globalization;

namespace XDrishti.Strategies.Mirror;

/// <summary>Decimal arithmetic on the text of a literal, so that <c>100 - 55.1</c> is <c>44.9</c> and not <c>44.900000000000006</c> (spec P3-02e, criterion 9).</summary>
internal static class LiteralMath
{
    private const int MaxDigits = 15;

    /// <summary>The value of <c>100 - value</c>, or null when either has more than 15 significant digits or the value is out of decimal range.</summary>
    public static double? Complement(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= 1e28 || !decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var exact))
        {
            return null;
        }

        var result = 100m - exact;
        if (Digits(exact) > MaxDigits || Digits(result) > MaxDigits)
        {
            return null;
        }

        return double.Parse(result.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static int Digits(decimal value)
    {
        var text = Math.Abs(value).ToString(CultureInfo.InvariantCulture).Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('0');
        if (value.Scale == 0)
        {
            text = text.TrimEnd('0');
        }

        return text.Length;
    }
}
