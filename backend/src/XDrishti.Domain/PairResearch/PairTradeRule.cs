namespace XDrishti.Domain.PairResearch;

/// <summary>
/// The sizing, the rupee risk and the R unit of a pair trade, and the entry rule (P14-13). Y-leg notional is <c>PairNotional</c>, X-leg notional <c>beta</c> times that, whole shares (floor; a leg of 0 shares is not sizable). Rupee risk is the Y notional times
/// <c>(stop - |z at entry|) x sd(W)</c>: the spread loss in log terms if the stop is reached. <c>R</c> is the net rupees of both legs over that risk. A pair opens only when <c>entry &lt;= |z| &lt; stop</c> (the state machine of the core would also open at or beyond the stop and stop out next bar, ADR 0192). Pure.
/// </summary>
public static class PairTradeRule
{
    /// <summary>Whole shares of each leg at the given prices; null when a price is not a number above 0, <paramref name="beta"/> is not above 0 or a leg is 0 shares (<c>not_sizable</c>).</summary>
    public static PairSizing? Size(decimal notional, double priceY, double priceX, double beta)
    {
        if (!(notional > 0) || !double.IsFinite(priceY) || !double.IsFinite(priceX) || !double.IsFinite(beta) || priceY <= 0 || priceX <= 0 || beta <= 0)
        {
            return null;
        }

        var y = Math.Floor((double)notional / priceY);
        var x = Math.Floor(beta * (double)notional / priceX);
        return y is >= 1 and <= int.MaxValue && x is >= 1 and <= int.MaxValue ? new PairSizing((int)y, (int)x) : null;
    }

    /// <summary>The rupee risk in paise precision; null when <c>|z| &gt;= stop</c> or a number is not finite or the risk is not above 0.</summary>
    public static decimal? RupeeRisk(decimal notional, double zEntry, double sdWindow, double stop)
    {
        var distance = stop - Math.Abs(zEntry);
        if (!double.IsFinite(zEntry) || !double.IsFinite(sdWindow) || !double.IsFinite(stop) || !(sdWindow > 0) || !(distance > 0))
        {
            return null;
        }

        var risk = Math.Round(notional * (decimal)(distance * sdWindow), 2, MidpointRounding.AwayFromZero);
        return risk > 0 ? risk : null;
    }

    /// <summary>R as net (or gross) rupees over the rupee risk.</summary>
    public static double R(decimal rupees, decimal risk) => risk > 0 ? (double)(rupees / risk) : double.NaN;

    /// <summary>Whether a pair may open at this z: <c>entry &lt;= |z| &lt; stop</c>.</summary>
    public static bool MayOpen(double z, double entry, double stop) => double.IsFinite(z) && Math.Abs(z) >= entry && Math.Abs(z) < stop;
}
