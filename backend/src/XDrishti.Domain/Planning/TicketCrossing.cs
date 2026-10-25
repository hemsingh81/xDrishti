using System.Globalization;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Planning;

/// <summary>
/// The pure rules of a price crossing on a watched (Armed) ticket (spec P7-20, ADR 0115). A firm tick inside the session that reaches or passes a stored numeric level is a crossing; equality counts. Nothing here knows an order, a broker or a ticket
/// state: a crossing is information for the owner, it never moves a ticket.
/// </summary>
public static class TicketCrossing
{
    /// <summary>The words every crossing text ends with.</summary>
    public const string NoOrderWording = "Alert only: no order is placed and none exists. A stop is only an alert.";

    /// <summary>The first instant of the continuous session, India time.</summary>
    public static readonly TimeSpan SessionOpen = new(9, 15, 0);

    /// <summary>The session ends here, India time; a tick at 15:30:00 or later fires nothing.</summary>
    public static readonly TimeSpan SessionClose = new(15, 30, 0);

    /// <summary>True for a tick stamped from 09:15:00 up to, not including, 15:30:00 India time.</summary>
    public static bool InSession(DateTimeOffset tickAt)
    {
        var time = IndiaTime.ToIst(tickAt).TimeOfDay;
        return time >= SessionOpen && time < SessionClose;
    }

    /// <summary>The price as it is stored and compared: four decimals, half away from zero.</summary>
    public static decimal Round(decimal price) => Math.Round(price, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The kinds that the price crosses (stop first). A BUY crosses its stop at or below the level and its target at or above; a SELL is the mirror. A null level can not fire. An unknown side fires nothing.
    /// </summary>
    public static IReadOnlyList<string> Crossed(string side, decimal? stop, decimal? target, decimal price)
    {
        if (side is not ("BUY" or "SELL") || price <= 0)
        {
            return [];
        }

        var buy = side == "BUY";
        var p = Round(price);
        var kinds = new List<string>(2);
        if (stop is { } s && (buy ? p <= s : p >= s))
        {
            kinds.Add(CrossingKinds.Stop);
        }

        if (target is { } t && (buy ? p >= t : p <= t))
        {
            kinds.Add(CrossingKinds.Target);
        }

        return kinds;
    }

    /// <summary>Whether a stored crossing still counts in the plan notices: until the end of the India calendar day after the day of its tick, then no longer (the row stays).</summary>
    public static bool IsLive(DateTimeOffset tickAt, DateTimeOffset now) => IndiaTime.Today(now) <= IndiaTime.Today(tickAt).AddDays(1);

    /// <summary>The words of one crossing on the ticket card. It never says a stop was hit and never says an order exists.</summary>
    public static string Text(string kind, string? stopAnchor, DateOnly session, decimal level, decimal price)
    {
        var lv = Number(level);
        var px = Number(price);
        var what = kind == CrossingKinds.Target
            ? $"target level {lv} crossed at {px}"
            : stopAnchor == "close_of_d"
                ? $"indicative stop from the close of {session:yyyy-MM-dd} crossed at {px} (level {lv})"
                : $"stop level {lv} crossed at {px}";
        return $"If you hold this trade: {what}. {NoOrderWording}";
    }

    /// <summary>The words of a crossing in the bell: a count and no symbol or price.</summary>
    public static string BellText(string kind) =>
        kind == CrossingKinds.Target
            ? "1 watched ticket crossed its target level; open the plan"
            : "1 watched ticket crossed its indicative stop level; open the plan";

    private static string Number(decimal value) => value.ToString("0.00##", CultureInfo.InvariantCulture);
}
