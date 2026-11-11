namespace XDrishti.Domain.Market.Strength;

/// <summary>One member's momentum return and daily volatility over the window, before ranking.</summary>
/// <param name="Symbol">The member.</param>
/// <param name="Return">R = c(t-s) / c(t-L) - 1.</param>
/// <param name="Volatility">The sample standard deviation of the daily returns over the same sessions.</param>
public sealed record MomentumInput(string Symbol, double Return, double Volatility);
