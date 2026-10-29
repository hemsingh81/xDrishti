namespace XDrishti.Execution.Domain.Protection;

/// <summary>
/// The pure rules that keep every protective action risk-reducing (spec P12-04 I3 and I4): a long's stop only moves up and stays two ticks below the last price; the modification cap holds; an exit never exceeds the position less the exits already in flight.
/// Prices are rounded toward the safe side (a sell limit down, a stop trigger down). There is no function here that loosens a stop.
/// </summary>
public static class StopRules
{
    public const decimal Tick = 0.05m;
    public const int MinTicksFromLast = 2;

    public static decimal FloorToTick(decimal price) => Math.Floor(price / Tick) * Tick;

    /// <summary>The limit of a stop-loss limit order: the trigger less the buffer, rounded down to the tick (1,485.00 less 0.5% is 1,477.55).</summary>
    public static decimal StopLimit(decimal trigger, decimal bufferPct) => FloorToTick(trigger * (1m - (bufferPct / 100m)));

    /// <summary>A marketable sell limit: the last price less the collar, rounded down to the tick.</summary>
    public static decimal MarketableSell(decimal last, decimal collarPct) => FloorToTick(last * (1m - (collarPct / 100m)));

    /// <summary>Null when the move is allowed, else the reason. <paramref name="modifications"/> is how many modifications the intent has had.</summary>
    public static string? CheckTighten(decimal currentTrigger, decimal newTrigger, decimal? last, int modifications, int cap)
    {
        if (newTrigger <= 0m || newTrigger % Tick != 0m)
        {
            return "price_or_tick";
        }

        if (newTrigger < currentTrigger)
        {
            return "stop_loosen";
        }

        if (newTrigger == currentTrigger)
        {
            return "no_change";
        }

        if (last is not { } price || price <= 0m)
        {
            return "no_last_price";
        }

        if (newTrigger > price - (MinTicksFromLast * Tick))
        {
            return "too_close_to_last";
        }

        return modifications >= cap ? "modification_cap" : null;
    }

    /// <summary>Entry plus one tick once the last price is at least <paramref name="atR"/> initial risks in favour; null when off (0), not yet reached, or the risk is not positive.</summary>
    public static decimal? BreakevenTrigger(decimal entry, decimal stop, decimal last, decimal atR)
    {
        var risk = entry - stop;
        return atR > 0m && risk > 0m && last >= entry + (atR * risk) ? entry + Tick : null;
    }

    /// <summary>The most an exit may sell now; never negative, zero when flat.</summary>
    public static int MaxExitQty(int netQty, int inFlight) => Math.Max(0, netQty - Math.Max(0, inFlight));

    /// <summary>The marketable price of flatten step <paramref name="step"/> (0-based): one step collar, widening by one step each time, never beyond the exit collar (the sender refuses a price outside it).</summary>
    public static decimal FlattenPrice(decimal last, decimal exitCollarPct, decimal stepCollarPct, int step) =>
        MarketableSell(last, Math.Min(exitCollarPct, stepCollarPct * (step + 1)));
}
