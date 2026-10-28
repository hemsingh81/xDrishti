using XDrishti.Domain.Research;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// Splits the entry slippage into the delay before the order and the execution itself (spec P11-02 definitions). With d = +1 for a buy and -1 for a sell: total = d x (VWAP - reference); delay = d x (arrival - reference); execution = d x (VWAP - arrival);
/// total = delay + execution. Pure, decimal arithmetic, so the identity is exact.
/// </summary>
public static class SlippageDecomposer
{
    private const decimal BasisPoints = 10_000m;

    /// <summary>The parts; null when the reference or the VWAP is not a positive price (nothing to measure against).</summary>
    public static SlippageParts? Decompose(Side side, decimal reference, decimal? arrival, decimal vwap, decimal? tick, decimal? riskPerShare)
    {
        if (reference <= 0 || vwap <= 0)
        {
            return null;
        }

        var d = side == Side.Buy ? 1m : -1m;
        var total = d * (vwap - reference);
        decimal? delay = null;
        decimal? execution = null;
        if (arrival is { } a && a > 0)
        {
            delay = d * (a - reference);
            execution = d * (vwap - a);
        }

        double? Ratio(decimal? rupees, decimal? per) => rupees is { } r && per is > 0 ? (double)(r / per.Value) : null;
        return new SlippageParts(
            total,
            delay,
            execution,
            Ratio(total, tick),
            (double)(total / reference * BasisPoints),
            Ratio(total, riskPerShare),
            Ratio(delay, riskPerShare),
            Ratio(execution, riskPerShare));
    }
}
