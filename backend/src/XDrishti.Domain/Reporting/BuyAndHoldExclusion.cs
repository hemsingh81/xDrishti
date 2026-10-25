namespace XDrishti.Domain.Reporting;

/// <summary>An instrument left out of a buy-and-hold figure and why (P8-07).</summary>
/// <param name="Id">The instrument.</param>
/// <param name="Symbol">The symbol.</param>
/// <param name="Reason">One of the <see cref="BuyAndHold"/> reason names: no_bars, too_few_bars, starts_late, ends_early, discontinuity, bad_price.</param>
public sealed record BuyAndHoldExclusion(Guid Id, string Symbol, string Reason);
