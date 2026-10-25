namespace XDrishti.Application.Reporting;

/// <summary>The stored out-of-sample exit choices (spec P8-10 part b, ADR 0054): which exit won each fold of a walk-forward, whether the choice is stable, and what the record itself says it is worth.</summary>
/// <param name="Selections">Newest first. Empty is an ordinary answer.</param>
/// <param name="Limits">What always holds, as text.</param>
public sealed record ReportExitSelectionsView(IReadOnlyList<ExitSelectionView> Selections, IReadOnlyList<string> Limits);

/// <summary>A stored exit choice for one cell. <c>Stable</c> is null when the record makes no statement, and <c>StabilityReason</c> says why.</summary>
/// <param name="SelectionKey">The selection key.</param>
/// <param name="ComparisonKey">The comparison it was made from.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="Timeframe">The cell's timeframe.</param>
/// <param name="Side">The cell's side.</param>
/// <param name="Style">The cell's style.</param>
/// <param name="Regime">The cell's regime (* until a regime exists).</param>
/// <param name="Unit">The unit of the scores.</param>
/// <param name="DefaultCandidateId">The exit used when no choice stands.</param>
/// <param name="ChosenCandidateId">The walk-forward choice; null when there is none.</param>
/// <param name="NoChoiceReason">Why there is none; null when there is one.</param>
/// <param name="Stable">True, false, or null for no statement.</param>
/// <param name="StabilityReason">The reason of the verdict.</param>
/// <param name="UsableFolds">Folds counted toward the verdict.</param>
/// <param name="AgreeingFolds">Of those, folds agreeing with the modal winner.</param>
/// <param name="PositiveFolds">Of the agreeing, folds positive out of sample.</param>
/// <param name="ChancePass">Exact probability that random winners would pass; null when not computed.</param>
/// <param name="LonePeaks">Folds won by a lone peak.</param>
/// <param name="Pooled">Pooled out-of-sample figures, each a state.</param>
/// <param name="Folds">The folds.</param>
/// <param name="Banner">The record's own caveats, verbatim.</param>
/// <param name="CreatedAt">When stored.</param>
/// <param name="Unreadable">Why the stored record could not be read; then no figure is given. Null for a readable record.</param>
/// <param name="MinTrades">The minimum sample the figures were judged against: the larger of the report minimum and the record's own.</param>
public sealed record ExitSelectionView(
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
    IReadOnlyList<PooledFigureView> Pooled,
    IReadOnlyList<SelectionFoldView> Folds,
    IReadOnlyList<string> Banner,
    DateTimeOffset CreatedAt,
    string? Unreadable,
    int MinTrades);

/// <summary>A pooled out-of-sample figure: the number of trades and the mean and score as states (a number only for a value).</summary>
/// <param name="Name">What was pooled.</param>
/// <param name="Trades">Out-of-sample trades.</param>
/// <param name="MeanR">Mean R.</param>
/// <param name="Score">Score.</param>
public sealed record PooledFigureView(string Name, int Trades, MetricView MeanR, MetricView Score);

/// <summary>One fold of a choice.</summary>
/// <param name="Index">1-based.</param>
/// <param name="TestStart">First test day.</param>
/// <param name="TestEnd">Last test day.</param>
/// <param name="Winner">The winning candidate; null when none qualified.</param>
/// <param name="WinnerTestTrades">Test trades of the winner.</param>
/// <param name="WinnerTestScore">The winner's out-of-sample score as a state.</param>
/// <param name="Thin">The fold does not count toward the verdict.</param>
public sealed record SelectionFoldView(int Index, DateOnly TestStart, DateOnly TestEnd, string? Winner, int WinnerTestTrades, MetricView WinnerTestScore, bool Thin);
