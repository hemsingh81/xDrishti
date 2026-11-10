namespace XDrishti.Domain.Reporting;

/// <summary>
/// The performance profile of a basket (BK-06, metrics m1b to m16). Every figure is a <see cref="Metric"/> with its honest state, or sits in a list that only exists when the figure does; nothing in it is a number for a state that has none.
/// </summary>
/// <param name="AsOf">The last session with a bar of any member; null when there is none.</param>
/// <param name="Index">The daily-rebalanced equal-weight index (m1b), with the instrument-days left out.</param>
/// <param name="Volatility">Annualised volatility per window (m3).</param>
/// <param name="Drawdown">Maximum drawdown and duration (m4).</param>
/// <param name="Benchmark">Beta, alpha, tracking error, information ratio and capture (m5 to m7).</param>
/// <param name="RelativeStrength">The relative strength line and returns (m8); the rank among baskets is added by the caller.</param>
/// <param name="Breadth">Breadth inside the basket on <paramref name="AsOf"/> (m9).</param>
/// <param name="Dispersion">Dispersion of member returns (m10).</param>
/// <param name="Correlation">Average pairwise correlation and effective number of bets (m11, m12).</param>
/// <param name="Turnover">Member turnover (m13).</param>
/// <param name="Capacity">Liquidity and capacity (m14).</param>
/// <param name="Contribution">Contribution to the buy-and-hold return (m15).</param>
/// <param name="Regime">The index by regime (m16).</param>
/// <param name="HoldReturnPct">The basket's buy-and-hold price return in percent (m1, P8-07).</param>
/// <param name="IndexReturnPct">The benchmark's price return over the same window in percent.</param>
/// <param name="DifferenceToIndex">HoldReturnPct minus IndexReturnPct, both passive over the same dates (Decision 6); never called alpha.</param>
/// <param name="MembershipBasis"><see cref="MemberTurnover.Dated"/> or <see cref="MemberTurnover.SingleSnapshot"/>: a single snapshot is survivorship biased (ISS-22).</param>
/// <param name="FirstDatedMembership">The first date with membership history.</param>
public sealed record BasketProfile(
    DateOnly? AsOf,
    IndexSeries Index,
    IReadOnlyList<VolatilityFigure> Volatility,
    DrawdownResult Drawdown,
    BenchmarkRelation Benchmark,
    RelativeStrengthResult RelativeStrength,
    BasketBreadthResult Breadth,
    DispersionResult Dispersion,
    CorrelationResult Correlation,
    TurnoverResult Turnover,
    BasketCapacityResult Capacity,
    ContributionResult Contribution,
    RegimeResult Regime,
    Metric HoldReturnPct,
    Metric IndexReturnPct,
    Metric DifferenceToIndex,
    string MembershipBasis,
    DateOnly? FirstDatedMembership);
