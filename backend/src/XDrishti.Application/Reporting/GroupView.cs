namespace XDrishti.Application.Reporting;

/// <summary>One group of a report on the wire: who it is, its trade count and every figure as a <see cref="MetricView"/> (a number only for value or provisional), with the evidence of the group.</summary>
/// <param name="Key">The coordinates as text, <c>strategy=ORB|side=BUY</c>; <c>all</c> for the whole population.</param>
/// <param name="Parts">The coordinates.</param>
/// <param name="N">Trades in the group.</param>
/// <param name="Wins">Trades with R above 0.</param>
/// <param name="WinRate">Wins over trades.</param>
/// <param name="AvgWinR">Mean R of the winners.</param>
/// <param name="AvgLossR">Mean R of the losers.</param>
/// <param name="ExpectancyR">Mean R per trade.</param>
/// <param name="ProfitFactor">Winning R over absolute losing R.</param>
/// <param name="TotalR">Sum of R.</param>
/// <param name="MaxDrawdownR">Largest peak-to-trough fall of the running R.</param>
/// <param name="LongestLossStreak">Most losing trades in a row.</param>
/// <param name="AvgHoldingSessions">Mean sessions held.</param>
/// <param name="ShrunkWinRate">The win rate shrunk toward its parent group.</param>
/// <param name="WinRateLower">Lower end of the credible interval of the shrunk win rate.</param>
/// <param name="ShrunkExpectancyR">Expectancy from the shrunk win rate and payoffs.</param>
/// <param name="ExpectancyLowerR">Expectancy from the lower credible win rate and the shrunk payoffs: the ranking figure.</param>
/// <param name="Evidence">The evidence of the group.</param>
public sealed record GroupView(
    string Key,
    IReadOnlyList<GroupPartView> Parts,
    int N,
    int Wins,
    MetricView WinRate,
    MetricView AvgWinR,
    MetricView AvgLossR,
    MetricView ExpectancyR,
    MetricView ProfitFactor,
    MetricView TotalR,
    MetricView MaxDrawdownR,
    MetricView LongestLossStreak,
    MetricView AvgHoldingSessions,
    MetricView ShrunkWinRate,
    MetricView WinRateLower,
    MetricView ShrunkExpectancyR,
    MetricView ExpectancyLowerR,
    GroupEvidenceView Evidence);
