namespace XDrishti.Domain.Options.Costs;

/// <summary>The two rupee thresholds of the liquidity filter (settings <c>Options:Liquidity:*</c>, both UNVERIFIED starting values): open-interest notional in crore and entry-bar turnover in lakh.</summary>
public sealed record LiquidityThresholds(decimal MinOiNotionalCr, decimal MinBarTurnoverLakh)
{
    public const decimal Crore = 10_000_000m;

    public const decimal Lakh = 100_000m;
}
