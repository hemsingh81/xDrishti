using XDrishti.Domain.Market.Actions;

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

    /// <summary>The traded value of a day bar in rupees, close times volume; -1 when the bar cannot give one (a close that is not a finite number, or a negative close or volume), which <see cref="Median"/> reads as no value.</summary>
    public static decimal TradedValue(DailyBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return double.IsFinite(bar.Close) && bar.Close >= 0 && bar.Volume >= 0 ? (decimal)bar.Close * bar.Volume : -1m;
    }

    /// <summary>
    /// The traded value of one stored minute bar in rupees, close times volume: the same definition as <see cref="TradedValue"/> of a day bar (ADR 0224). Null when the minute cannot give a usable figure, which the capacity check reads as unavailable
    /// and never as a pass: a close that is not a finite positive number, or a volume of zero or less (a minute with no volume has no value to hold an order against). Pure.
    /// </summary>
    public static decimal? MinuteTradedValue(double close, long volume) =>
        double.IsFinite(close) && close > 0 && volume > 0 && close < (double)(decimal.MaxValue / volume) ? (decimal)close * volume : null;

    /// <summary>
    /// The tier and the capacity check of one order from day bars (CF-05, D2 and D4, ADR 0223): the tier is of the 20-day median traded value of the sessions strictly before <paramref name="entryDate"/> (what was known at the entry; unavailable
    /// with fewer than 20 usable sessions). The capacity holds the order against <paramref name="dailyShare"/> of that median, and against <paramref name="fillShare"/> of <paramref name="fillMinuteValue"/>, the fill minute's traded value, which no day bar can give: it is
    /// null when the fill minute could not be read (ADR 0224: <see cref="MinuteTradedValue"/> of the stored minute bar), and the minute cap then reads unavailable (fail closed, never a pass), while a breach of the median cap is still a breach. The entry day's traded value is only returned as information
    /// (<c>OverEntryDayShare</c>: the order against <paramref name="fillShare"/> of the day, about 375 times looser than the minute); it can never make a pass. Pure.
    /// </summary>
    /// <param name="orderValue">Quantity times the entry price in rupees; null when not known.</param>
    /// <param name="bars">Day bars of the instrument, oldest first, covering the sessions before the entry date and the entry date.</param>
    /// <param name="entryDate">The India date of the entry.</param>
    /// <param name="aCut">The tier A cut in rupees.</param>
    /// <param name="bCut">The tier B cut in rupees.</param>
    /// <param name="fillShare">The share of the fill minute's traded value an order may be.</param>
    /// <param name="dailyShare">The share of the 20-day median an order may be.</param>
    /// <param name="fillMinuteValue">The fill minute's traded value in rupees; null when it is not read.</param>
    public static (LiquidityTier Tier, CapacityCheck Capacity, bool? OverEntryDayShare) Assess(decimal? orderValue, IReadOnlyList<DailyBar> bars, DateOnly entryDate, decimal aCut, decimal bCut, decimal fillShare, decimal dailyShare, decimal? fillMinuteValue = null)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var prior = bars.Where(b => b.Date < entryDate).Select(TradedValue).ToList();
        var median = Median(prior);
        var tier = Of(median, aCut, bCut);
        if (orderValue is not { } order)
        {
            return (tier, new CapacityCheck(true, null, null, "unavailable: the order value is not known"), null);
        }

        var capacity = Capacity.Check(order, fillMinuteValue, median, fillShare, dailyShare);
        bool? overDay = bars.FirstOrDefault(b => b.Date == entryDate) is { } day && TradedValue(day) is var v and >= 0 ? order > v * fillShare : null;
        return (tier, capacity, overDay);
    }

    /// <summary>The slippage floor in bps of the price for a tier; null for tier C and unavailable (not priced).</summary>
    public static decimal? Bps(LiquidityTier tier, decimal aBps, decimal bBps) => tier switch
    {
        LiquidityTier.A => aBps,
        LiquidityTier.B => bBps,
        _ => null,
    };
}
