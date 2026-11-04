namespace XDrishti.Domain.Research;

/// <summary>
/// The tier of an instrument by its 20-day median traded value (close x volume per session) on the entry date (CF-05, D2). The cuts (proposed Rs 100 crore and Rs 20 crore) and the bps (3 and 8) are planning numbers to be replaced by measurement from paper fills;
/// they are arguments, not constants of the simulation. Pure.
/// </summary>
public static class LiquidityTiers
{
    /// <summary>One crore in rupees.</summary>
    public const decimal Crore = 10_000_000m;

    /// <summary>The sessions the median is taken over.</summary>
    public const int MedianSessions = 20;

    /// <summary>The median of the last <see cref="MedianSessions"/> values of <paramref name="dailyValues"/> (oldest first); null when there are fewer than that or a value is negative.</summary>
    public static decimal? Median(IReadOnlyList<decimal> dailyValues)
    {
        ArgumentNullException.ThrowIfNull(dailyValues);
        if (dailyValues.Count < MedianSessions)
        {
            return null;
        }

        var last = new decimal[MedianSessions];
        for (var i = 0; i < MedianSessions; i++)
        {
            var v = dailyValues[dailyValues.Count - MedianSessions + i];
            if (v < 0)
            {
                return null;
            }

            last[i] = v;
        }

        Array.Sort(last);
        return (last[(MedianSessions / 2) - 1] + last[MedianSessions / 2]) / 2m;
    }

    /// <summary>The tier of a median traded value in rupees against the cuts in rupees.</summary>
    public static LiquidityTier Of(decimal? medianValue, decimal aCut, decimal bCut) =>
        medianValue is not { } v ? LiquidityTier.Unavailable : v >= aCut ? LiquidityTier.A : v >= bCut ? LiquidityTier.B : LiquidityTier.C;

    /// <summary>The slippage floor in bps of the price for a tier; null for tier C and unavailable (not priced).</summary>
    public static decimal? Bps(LiquidityTier tier, decimal aBps, decimal bBps) => tier switch
    {
        LiquidityTier.A => aBps,
        LiquidityTier.B => bBps,
        _ => null,
    };
}
