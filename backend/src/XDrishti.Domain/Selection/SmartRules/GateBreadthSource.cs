using System.Globalization;

namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>What the gate reads as breadth from a stated basket, and the sentence that names the source.</summary>
/// <param name="Breadth">Share (0 to 1) above the 20-day average; null when unavailable: never a pass, never a fall-back to the tracked set.</param>
/// <param name="Note">"breadth of Nifty 50: 0.62", or why it is unavailable; names the basket either way.</param>
/// <param name="Reason"></param>
public sealed record GateBreadthRead(double? Breadth, string Note, string Reason = "ok");

/// <summary>
/// The breadth the market gate reads when the owner chose a basket (spec BK-08, criterion 9, ADR 0260): the 20-day breadth of the newest stored row before the decision date. No row, a row not before the decision date, a row older than <c>GateMaxAgeDays</c> or a row
/// with too few voters is unavailable. There is no fall-back to the tracked set: that would hide the change of definition on a bad data day. Pure.
/// </summary>
public static class GateBreadthSource
{
    /// <summary>Reads the stored row's figures.</summary>
    /// <param name="basketName">The basket's name, for the sentence.</param>
    /// <param name="rowDate">The row's day; null when there is no row.</param>
    /// <param name="b20">The row's 20-day breadth.</param>
    /// <param name="state">The row's state, <c>value</c> or <c>insufficient</c>.</param>
    /// <param name="decisionDate">The planned session.</param>
    /// <param name="maxAgeDays">The gate's maximum age.</param>
    public static GateBreadthRead FromBasket(string basketName, DateOnly? rowDate, double? b20, string? state, DateOnly decisionDate, int maxAgeDays)
    {
        var name = string.IsNullOrWhiteSpace(basketName) ? "the gate basket" : basketName.Trim();
        if (rowDate is not { } date)
        {
            return Unavailable(name, "no stored row", "no_row");
        }

        if (date >= decisionDate)
        {
            return Unavailable(name, "the newest row is not before the planned day", "not_before_day");
        }

        if (date < decisionDate.AddDays(-maxAgeDays))
        {
            return Unavailable(name, "the newest row is stale", "stale");
        }

        if (state != "value" || b20 is not { } value || !double.IsFinite(value))
        {
            return Unavailable(name, "too few members voted", "insufficient");
        }

        return new GateBreadthRead(value, string.Create(CultureInfo.InvariantCulture, $"breadth of {name}: {value:0.###}"));
    }

    private static GateBreadthRead Unavailable(string name, string why, string reason) => new(null, $"breadth of {name} is unavailable ({why}); the gate does not fall back to the tracked set", reason);
}
