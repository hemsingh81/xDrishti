using System.Globalization;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Alerts;

/// <summary>The words of the alert messages, audit details and notices: invariant culture, a price as ₹1,234.50.</summary>
internal static class AlertText
{
    /// <summary>₹1,234.5 · ₹1,200 · ₹98.1234 (up to four decimals, none when whole).</summary>
    public static string Price(decimal price) => string.Create(CultureInfo.InvariantCulture, $"₹{price:#,##0.####}");

    /// <summary>"at or below" · "at or above".</summary>
    public static string Condition(AlertDirection direction) => direction == AlertDirection.AtOrBelow ? "at or below" : "at or above";
}
