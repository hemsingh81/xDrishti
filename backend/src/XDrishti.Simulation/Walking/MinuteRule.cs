using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// The per-minute decision of the exit table, written once (ADR 0038 X2 to X5, used unchanged by every date of a swing walk, ADR 0040 D1): for one minute that holds a trade, in this order, an open already beyond the target with
/// the target reached fills at the target (the first print decides); a reached stop leaves at the stop or at the open when the minute gapped through it (a minute that reaches both is this row, ambiguous when the open lies strictly
/// between them); a reached target leaves at the target and never at a better price; in the fill minute of an entry at the limit only the stop is checked. The intraday loop and the swing loop both call <see cref="Decide"/>, so a change
/// of the rule is made here and nowhere else.
/// </summary>
internal static class MinuteRule
{
    /// <summary>Decides one minute. <paramref name="low"/> and <paramref name="high"/> are the extremes that count for the excursions before this minute (the adverse one is the low of a buy and the high of a sell).</summary>
    public static MinuteVerdict Decide(in Position position, in Minute minute, bool isFill, decimal low, decimal high)
    {
        var isBuy = position.IsBuy;
        var stop = position.Stop;
        var hasTarget = position.Target.HasValue;
        var target = position.Target.GetValueOrDefault();

        // X5: in the fill minute of an entry at the limit only the stop is checked; the favourable extreme of that minute may have come before the fill.
        var atLimitFill = isFill && position.EntryFill == EntryFill.AtLimit;
        var stopHit = Beyond(isBuy, isBuy ? minute.Low : minute.High, stop);
        var targetHit = hasTarget && !atLimitFill && Reaches(isBuy, isBuy ? minute.High : minute.Low, target);
        var openBeyondTarget = hasTarget && Reaches(isBuy, minute.Open, target);
        var targetGap = hasTarget && (isBuy ? minute.Open > target : minute.Open < target);

        if (targetHit && openBeyondTarget)
        {
            // X3 and the exception of X4: the first print is beyond the target, so the target fills there, whatever the minute did next; the minute contributes no range.
            var (targetLow, targetHigh) = Raise(isBuy, low, high, target);
            return new MinuteVerdict(true, ExitReason.Target, target, targetLow, targetHigh, new ExitFlags(TargetGap: targetGap));
        }

        if (stopHit)
        {
            // X2 and X4: the stop, or the open when the market gapped through it; a minute that reaches both is this row, and ambiguous when the open lies strictly between them.
            var exit = isBuy ? Math.Min(stop, minute.Open) : Math.Max(stop, minute.Open);
            var (stopLow, stopHigh) = Worsen(isBuy, low, high, exit);
            var flags = new ExitFlags(StopGap: isBuy ? minute.Open < stop : minute.Open > stop, Ambiguous: targetHit && !Beyond(isBuy, minute.Open, stop));
            return new MinuteVerdict(true, ExitReason.Stop, exit, stopLow, stopHigh, flags);
        }

        if (targetHit)
        {
            // X3: the target, never a better price; the adverse side of the minute counts in full.
            var (targetLow, targetHigh) = Raise(isBuy, low, high, target);
            var (adverseLow, adverseHigh) = Worsen(isBuy, targetLow, targetHigh, isBuy ? minute.Low : minute.High);
            return new MinuteVerdict(true, ExitReason.Target, target, adverseLow, adverseHigh, new ExitFlags(TargetGap: targetGap));
        }

        var (nextLow, nextHigh) = atLimitFill ? Worsen(isBuy, low, high, isBuy ? minute.Low : minute.High) : (Math.Min(low, minute.Low), Math.Max(high, minute.High));
        return new MinuteVerdict(false, default, 0m, nextLow, nextHigh, default);
    }

    /// <summary>The extremes after the adverse side reaches <paramref name="price"/>: the low of a buy, the high of a sell.</summary>
    public static (decimal Low, decimal High) Worsen(bool isBuy, decimal low, decimal high, decimal price) =>
        isBuy ? (Math.Min(low, price), high) : (low, Math.Max(high, price));

    /// <summary>The extremes after the favourable side reaches <paramref name="price"/>: the high of a buy, the low of a sell.</summary>
    public static (decimal Low, decimal High) Raise(bool isBuy, decimal low, decimal high, decimal price) =>
        isBuy ? (low, Math.Max(high, price)) : (Math.Min(low, price), high);

    /// <summary>A price at or beyond the stop (a buy: at or below it).</summary>
    public static bool Beyond(bool isBuy, decimal price, decimal stop) => isBuy ? price <= stop : price >= stop;

    /// <summary>A price at or beyond the target (a buy: at or above it).</summary>
    public static bool Reaches(bool isBuy, decimal price, decimal target) => isBuy ? price >= target : price <= target;
}
