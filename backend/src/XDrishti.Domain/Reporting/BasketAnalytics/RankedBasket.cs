namespace XDrishti.Domain.Reporting;

/// <summary>One row of the ranking board (BK-07). A basket whose input is not a value has no rank, only the state.</summary>
/// <param name="Id">The basket.</param>
/// <param name="RelativeStrength">The relative strength returns (m8).</param>
/// <param name="RiskAdjusted">The index return over the window divided by its annualised volatility, or why there is none.</param>
/// <param name="Rank">1 = strongest on the rank window; a state without a number when the basket has no value there.</param>
public sealed record RankedBasket(Guid Id, IReadOnlyList<RelativeStrengthReturn> RelativeStrength, Metric RiskAdjusted, Metric Rank);
