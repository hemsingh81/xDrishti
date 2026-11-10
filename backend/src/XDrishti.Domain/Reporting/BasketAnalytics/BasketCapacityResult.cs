namespace XDrishti.Domain.Reporting;

/// <summary>The liquidity and capacity of a basket (BK-06 m14).</summary>
/// <param name="MedianAdv">Median of the members' ADV20.</param>
/// <param name="P10Adv">10th percentile of the members' ADV20 (linear interpolation).</param>
/// <param name="BasketCapacity">The sum of the capacity share of each member's ADV20.</param>
/// <param name="AsmGsmShare">Share of counted members under ASM or GSM.</param>
/// <param name="FnoShare">Share of counted members in F&amp;O.</param>
/// <param name="Counted">Members with a full window of volume.</param>
/// <param name="ShortMembers">Members with fewer sessions than the window, listed and not counted.</param>
/// <param name="AdvBySymbol">ADV20 per counted member, for the correlation sample.</param>
public sealed record BasketCapacityResult(Metric MedianAdv, Metric P10Adv, Metric BasketCapacity, Metric AsmGsmShare, Metric FnoShare, int Counted, IReadOnlyList<string> ShortMembers, IReadOnlyDictionary<string, double> AdvBySymbol);
