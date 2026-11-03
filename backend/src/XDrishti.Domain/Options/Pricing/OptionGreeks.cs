namespace XDrishti.Domain.Options.Pricing;

/// <summary>
/// The Greeks in the units fixed by P14-05: delta and gamma per rupee of the underlying spot, <b>vega per 1 volatility point</b> (sigma + 0.01), <b>theta per calendar day</b> (the annual theta divided by 365).
/// </summary>
public readonly record struct OptionGreeks(double Delta, double Gamma, double Vega, double Theta);
