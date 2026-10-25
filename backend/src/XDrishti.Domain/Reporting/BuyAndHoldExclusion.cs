namespace XDrishti.Domain.Reporting;

/// <summary>An instrument left out of a buy-and-hold figure and why (P8-07).</summary>
/// <param name="Symbol">The symbol.</param>
/// <param name="Reason">One of the <see cref="BuyAndHold"/> reason names: no_bars, starts_late, ends_early, discontinuity, bad_price.</param>
public sealed record BuyAndHoldExclusion(string Symbol, string Reason);
