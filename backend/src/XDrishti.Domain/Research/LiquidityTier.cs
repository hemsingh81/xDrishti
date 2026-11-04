namespace XDrishti.Domain.Research;

/// <summary>The liquidity tier of an instrument on a date (CF-05, D2): A, B, C (excluded from edge claims, not priced) or unavailable (no history to judge from; never a tier).</summary>
public enum LiquidityTier
{
    /// <summary>The 20-day median traded value is at or above the A cut.</summary>
    A = 0,

    /// <summary>At or above the B cut and below the A cut.</summary>
    B = 1,

    /// <summary>Below the B cut: excluded from edge claims.</summary>
    C = 2,

    /// <summary>There is no usable median.</summary>
    Unavailable = 3,
}
