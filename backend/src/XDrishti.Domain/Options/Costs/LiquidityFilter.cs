namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// The rupee liquidity filter (spec P14-07): open interest times spot at least the OI threshold <b>and</b> the entry bar's volume times spot at least the bar-turnover threshold. A null open interest or volume, or a spot that is not above zero, is
/// <c>liquidity_unknown</c>: never a pass. Rupee thresholds need no historical lot. Pure.
/// </summary>
public static class LiquidityFilter
{
    public const string PassCode = "pass";

    public const string FilteredCode = "filtered";

    public const string UnknownCode = "liquidity_unknown";

    public static LiquidityResult Evaluate(long? openInterestUnits, long? barVolumeUnits, decimal spotRupees, LiquidityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        if (openInterestUnits is null or < 0 || barVolumeUnits is null or < 0 || spotRupees <= 0)
        {
            return new LiquidityResult(LiquidityVerdict.Unknown, UnknownCode, null, null);
        }

        var oi = openInterestUnits.Value * spotRupees;
        var turnover = barVolumeUnits.Value * spotRupees;
        var passes = oi >= thresholds.MinOiNotionalCr * LiquidityThresholds.Crore && turnover >= thresholds.MinBarTurnoverLakh * LiquidityThresholds.Lakh;
        return new LiquidityResult(passes ? LiquidityVerdict.Pass : LiquidityVerdict.Filtered, passes ? PassCode : FilteredCode, oi, turnover);
    }
}
