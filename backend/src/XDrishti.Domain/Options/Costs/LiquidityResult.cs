namespace XDrishti.Domain.Options.Costs;

/// <summary>The liquidity of one strike at one entry bar: the verdict, its word, and the two rupee figures when they could be computed.</summary>
public sealed record LiquidityResult(LiquidityVerdict Verdict, string Code, decimal? OiNotionalRupees, decimal? BarTurnoverRupees);
