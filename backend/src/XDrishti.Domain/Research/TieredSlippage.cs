namespace XDrishti.Domain.Research;

/// <summary>
/// The slippage floor of the conservative option by liquidity tier (CF-05, D2, ADR 0229): tier A 3 bps, tier B 8 bps of the price. Tier C is not priced and an unavailable tier is never priced as A: both give no bps, and the trade is excluded and counted.
/// The numbers are the defaults of the settings <c>Confidence:CostStress:TierABps</c>, <c>TierBBps</c> and <c>RangeFraction</c> (ADR 0230); they are judgment calls with no authoritative NSE source, to be replaced by measured paper fills (P11-02). Pure.
/// </summary>
public static class TieredSlippage
{
    /// <summary>Tier A floor default, in basis points of the price.</summary>
    public const decimal TierABps = 3m;

    /// <summary>Tier B floor default, in basis points of the price.</summary>
    public const decimal TierBBps = 8m;

    /// <summary>The default bar range share of the conservative preset (CF-05, D2): twice the default 0.05.</summary>
    public const double BarRangeFrac = 0.10;

    /// <summary>The bps for a tier; null for tier C and for an unavailable tier (the trade is excluded). Reuses <see cref="LiquidityTiers.Bps"/>.</summary>
    public static decimal? BpsOf(LiquidityTier tier) => LiquidityTiers.Bps(tier, TierABps, TierBBps);
}
