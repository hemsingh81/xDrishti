namespace XDrishti.Domain.Reporting;

/// <summary>
/// One group (a cell of a pivot) with its figures. Every figure is a <see cref="Metric"/>: below the minimum sample, or with a coordinate that was not recorded, no figure carries a number. <see cref="N"/> is a plain count.
/// The maximum drawdown and the streak follow the order of the exits (date, then run key, then trade key).
/// </summary>
/// <param name="Key">Who the group is.</param>
/// <param name="N">Trades in the group.</param>
/// <param name="Wins">Trades with R above 0 (a trade at exactly 0 is not a win).</param>
/// <param name="WinRate">Wins over trades, unshrunk.</param>
/// <param name="AvgWinR">Mean R of the winners.</param>
/// <param name="AvgLossR">Mean R of the losers (negative).</param>
/// <param name="ExpectancyR">Mean R per trade.</param>
/// <param name="ProfitFactor">Winning R over the absolute losing R.</param>
/// <param name="TotalR">Sum of R.</param>
/// <param name="MaxDrawdownR">Largest peak-to-trough fall of the running R.</param>
/// <param name="LongestLossStreak">Most losing trades in a row.</param>
/// <param name="AvgHoldingSessions">Mean sessions held.</param>
/// <param name="ShrunkWinRate">The win rate shrunk toward the parent group (P6-01 Beta posterior mean).</param>
/// <param name="WinRateLower">Lower end of the credible interval of the shrunk win rate.</param>
/// <param name="ShrunkExpectancyR">Expectancy from the shrunk win rate and the shrunk mean win and mean non-win.</param>
/// <param name="ExpectancyLowerR">Expectancy from the lower credible win rate and the shrunk mean win and non-win; the ranking figure. Not formed below the minimum number of wins and non-wins.</param>
/// <param name="PriorRate">The parent group's win rate the group was shrunk toward; null where no estimate was made.</param>
/// <param name="PriorAvgWinR">The parent group's mean win the group's mean win was shrunk toward; null where none was made.</param>
/// <param name="PriorAvgNonWinR">The parent group's mean non-win (R of 0 or less) the group's was shrunk toward; null where none was made.</param>
/// <param name="Evidence">The evidence block of the group.</param>
public sealed record ReportGroup(
    GroupKey Key,
    int N,
    int Wins,
    Metric WinRate,
    Metric AvgWinR,
    Metric AvgLossR,
    Metric ExpectancyR,
    Metric ProfitFactor,
    Metric TotalR,
    Metric MaxDrawdownR,
    Metric LongestLossStreak,
    Metric AvgHoldingSessions,
    Metric ShrunkWinRate,
    Metric WinRateLower,
    Metric ShrunkExpectancyR,
    Metric ExpectancyLowerR,
    double? PriorRate,
    double? PriorAvgWinR,
    double? PriorAvgNonWinR,
    EvidenceBlock Evidence)
{
    /// <summary>One figure by name. <see cref="ReportMetric.Trades"/> is a count: a fact for any group that has a trade, empty for none.</summary>
    public Metric Get(ReportMetric metric) => metric switch
    {
        ReportMetric.Trades => N > 0 ? Metric.Of(N) : Metric.Empty("no trades"),
        ReportMetric.WinRate => WinRate,
        ReportMetric.AvgWinR => AvgWinR,
        ReportMetric.AvgLossR => AvgLossR,
        ReportMetric.ExpectancyR => ExpectancyR,
        ReportMetric.ProfitFactor => ProfitFactor,
        ReportMetric.TotalR => TotalR,
        ReportMetric.MaxDrawdownR => MaxDrawdownR,
        ReportMetric.LongestLossStreak => LongestLossStreak,
        ReportMetric.AvgHoldingSessions => AvgHoldingSessions,
        ReportMetric.ShrunkWinRate => ShrunkWinRate,
        ReportMetric.WinRateLower => WinRateLower,
        ReportMetric.ShrunkExpectancyR => ShrunkExpectancyR,
        ReportMetric.ExpectancyLowerR => ExpectancyLowerR,
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };
}
