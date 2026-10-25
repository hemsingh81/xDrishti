using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.SimExport.Cards;

/// <summary>The number, time and date formats of the cards (spec P3-04c, C3 and C4): invariant culture, India time, no clock and no machine fact. A number is never built from free text.</summary>
internal static class CardFormat
{
    /// <summary>A price: at most 4 decimals, trailing zeros trimmed.</summary>
    public static string Price(double value) => Trimmed(value, 4, "0.####");

    /// <summary>A value with up to 6 significant digits, invariant culture (the values a condition read).</summary>
    public static string Significant(double value) => value.ToString("G6", CultureInfo.InvariantCulture);

    /// <summary>A coordinate of the chart: at most 2 decimals, trailing zeros trimmed.</summary>
    public static string Coord(double value) => Trimmed(value, 2, "0.##");

    /// <summary>R with 3 decimals and an explicit sign (<c>+2.000</c>, <c>-1.100</c>, <c>0.000</c>).</summary>
    public static string SignedR(double value)
    {
        var rounded = FiniteRound(value, 3);
        return rounded == 0 ? "0.000" : rounded.ToString("+0.000;-0.000", CultureInfo.InvariantCulture);
    }

    /// <summary>R with 3 decimals, no sign for a positive value.</summary>
    public static string R(double value)
    {
        var rounded = FiniteRound(value, 3);
        return (rounded == 0 ? 0 : rounded).ToString("0.000", CultureInfo.InvariantCulture);
    }

    /// <summary>Rupees with 2 decimals.</summary>
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A whole number.</summary>
    public static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A decimal as it is stored (the tick, a ratio of a split), trailing zeros trimmed.</summary>
    public static string Decimal(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>A date as <c>yyyy-MM-dd</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary><c>HH:mm</c> India time of an instant (epoch seconds).</summary>
    public static string Clock(long epochSeconds)
    {
        var minute = SessionClock.MinuteOfDay(epochSeconds);
        return string.Create(CultureInfo.InvariantCulture, $"{minute / 60:00}:{minute % 60:00}");
    }

    /// <summary>The India date of an instant.</summary>
    public static DateOnly DateOf(long epochSeconds) => SessionClock.DateOf(epochSeconds);

    /// <summary><c>HH:mm</c> of a minute of the session, counted from 09:15.</summary>
    public static string ClockAtOffset(int minuteOffset)
    {
        var minute = SessionClock.OpenMinute + minuteOffset;
        return string.Create(CultureInfo.InvariantCulture, $"{minute / 60:00}:{minute % 60:00}");
    }

    private static string Trimmed(double value, int decimals, string format)
    {
        var rounded = FiniteRound(value, decimals);
        return (rounded == 0 ? 0 : rounded).ToString(format, CultureInfo.InvariantCulture);
    }

    private static double FiniteRound(double value, int decimals) =>
        double.IsFinite(value) ? Math.Round(value, decimals, MidpointRounding.AwayFromZero) : throw new ArgumentException("A NaN or an infinity cannot be printed on a card.", nameof(value));
}
