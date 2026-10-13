using System.Globalization;

namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>The words and numbers of the item texts, in one place: invariant culture, rupees in lakhs and crores, percentages with one decimal.</summary>
internal static class ActionText
{
    private const decimal Lakh = 100_000m;

    private const decimal Crore = 10_000_000m;

    /// <summary>₹45,000 · ₹1.20 L (from a lakh) · ₹2.35 Cr (from a crore).</summary>
    public static string Rupees(decimal amount)
    {
        var abs = Math.Abs(amount);
        var sign = amount < 0m ? "-" : string.Empty;
        return abs >= Crore
            ? string.Create(CultureInfo.InvariantCulture, $"{sign}₹{abs / Crore:0.00} Cr")
            : abs >= Lakh
                ? string.Create(CultureInfo.InvariantCulture, $"{sign}₹{abs / Lakh:0.00} L")
                : string.Create(CultureInfo.InvariantCulture, $"{sign}₹{abs:#,##0}");
    }

    /// <summary>23.4 %</summary>
    public static string Percent(decimal value) => string.Create(CultureInfo.InvariantCulture, $"{value:0.0} %");

    /// <summary>12 Nov 2026</summary>
    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Plural(int count, string singular, string plural) => string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? singular : plural)}");
}
