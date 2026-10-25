namespace XDrishti.Application.Abstractions.Reporting;

/// <summary>
/// Where the reports read the stored exit comparisons and exit choices (spec P8-10 part b, ADR 0114): header columns of <c>research.sim_comparisons</c> (never the CSV text) and the stored record of <c>research.exit_selections</c>. A sibling of
/// <see cref="IReportTradeSource"/>, so the trade port and its fakes stay as they are. Read only.
/// </summary>
public interface IReportComparisonSource
{
    /// <summary>Every stored comparison, newest first.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    ValueTask<IReadOnlyList<ReportComparisonInfo>> ListComparisonsAsync(CancellationToken cancellationToken);

    /// <summary>Every stored exit choice, newest first, with what its record says.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    ValueTask<IReadOnlyList<ReportExitSelectionInfo>> ListExitSelectionsAsync(CancellationToken cancellationToken);
}

/// <summary>The header of a stored comparison of exits on the same entries.</summary>
/// <param name="ComparisonKey">The comparison key (64 hex).</param>
/// <param name="EntriesKey">The key of the common entries (64 hex).</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="Unit">The unit of R the comparison was made in (RFilled or RNet).</param>
/// <param name="CommonEntries">Entries every member shares.</param>
/// <param name="Members">The member runs in order, with the candidate each ran.</param>
/// <param name="CreatedAt">When it was stored.</param>
public sealed record ReportComparisonInfo(string ComparisonKey, string EntriesKey, string StrategyId, string Unit, int CommonEntries, IReadOnlyList<ReportComparisonMember> Members, DateTimeOffset CreatedAt);

/// <summary>One member of a stored comparison.</summary>
/// <param name="RunKey">The stored run key.</param>
/// <param name="CandidateId">The exit candidate id.</param>
public sealed record ReportComparisonMember(string RunKey, string CandidateId);

/// <summary>A stored exit choice for one cell, as its record states it (ADR 0054).</summary>
/// <param name="SelectionKey">The selection key (64 hex).</param>
/// <param name="ComparisonKey">The comparison it was made from.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="Timeframe">The cell's timeframe.</param>
/// <param name="Side">The cell's side.</param>
/// <param name="Style">The cell's style.</param>
/// <param name="Regime">The cell's regime (* until a regime exists).</param>
/// <param name="Unit">The unit of the scores.</param>
/// <param name="DefaultCandidateId">The first candidate of the comparison, which is used when no choice stands.</param>
/// <param name="ChosenCandidateId">The walk-forward choice; null when there is no choice.</param>
/// <param name="NoChoiceReason">Why there is no choice; null when there is one.</param>
/// <param name="Stable">The stability verdict; null when no statement could be made.</param>
/// <param name="StabilityReason">The reason of the verdict.</param>
/// <param name="UsableFolds">Folds that count toward the verdict.</param>
/// <param name="AgreeingFolds">Of those, folds that agree with the modal winner.</param>
/// <param name="PositiveFolds">Of the agreeing, folds whose winner scored positive out of sample.</param>
/// <param name="ChancePass">The exact probability that uniformly random winners pass the agreement rule; null when not computed.</param>
/// <param name="LonePeaks">Folds won by a lone peak.</param>
/// <param name="Pooled">The pooled out-of-sample figures: the walk-forward choice, the default and every single candidate.</param>
/// <param name="Folds">The folds.</param>
/// <param name="Banner">The sentences the record prints with the verdict.</param>
/// <param name="CreatedAt">When it was stored.</param>
public sealed record ReportExitSelectionInfo(
    string SelectionKey,
    string ComparisonKey,
    string StrategyId,
    string Timeframe,
    string Side,
    string Style,
    string Regime,
    string Unit,
    string DefaultCandidateId,
    string? ChosenCandidateId,
    string? NoChoiceReason,
    bool? Stable,
    string StabilityReason,
    int UsableFolds,
    int AgreeingFolds,
    int PositiveFolds,
    double? ChancePass,
    int LonePeaks,
    IReadOnlyList<ReportPooledFigure> Pooled,
    IReadOnlyList<ReportSelectionFold> Folds,
    IReadOnlyList<string> Banner,
    DateTimeOffset CreatedAt);

/// <summary>A pooled out-of-sample figure of a stored choice.</summary>
/// <param name="Name">What was pooled (the walk-forward choice, the default, a candidate).</param>
/// <param name="Trades">Out-of-sample trades behind it.</param>
/// <param name="MeanR">Mean R; null when the record has none.</param>
/// <param name="Score">The score (mean R minus the downside penalty); null when the record has none.</param>
public sealed record ReportPooledFigure(string Name, int Trades, double? MeanR, double? Score);

/// <summary>One fold of a stored choice.</summary>
/// <param name="Index">1-based fold number.</param>
/// <param name="TestStart">First day of the test window.</param>
/// <param name="TestEnd">Last day of the test window.</param>
/// <param name="Winner">The candidate that won the fold; null when none qualified.</param>
/// <param name="WinnerTestTrades">Test trades of the winner.</param>
/// <param name="WinnerTestScore">The winner's out-of-sample score; null when none.</param>
/// <param name="Thin">The winner had too few test trades for the fold to count.</param>
public sealed record ReportSelectionFold(int Index, DateOnly TestStart, DateOnly TestEnd, string? Winner, int WinnerTestTrades, double? WinnerTestScore, bool Thin);
