namespace XDrishti.Domain.Research;

/// <summary>One candidate in one fold: whether it qualified and its train and test figures (D5, D9). Scores are <c>mean_R - lambda * downside_dev_R</c>; null when the sample is empty.</summary>
/// <param name="Id">The candidate id.</param>
/// <param name="Qualified">At least the minimum of trades in the train set.</param>
/// <param name="TrainTrades">Trades in the train set.</param>
/// <param name="TrainMeanR">Mean R of the train set.</param>
/// <param name="TrainScore">Score on the train set.</param>
/// <param name="TestTrades">Trades in the test window.</param>
/// <param name="TestScore">Score on the test window.</param>
public sealed record FoldCandidateRow(string Id, bool Qualified, int TrainTrades, double? TrainMeanR, double? TrainScore, int TestTrades, double? TestScore);

/// <summary>One fold of the walk-forward (D2, D5, D7): the dates, the counts, the winner and its figures.</summary>
/// <param name="Index">1-based.</param>
/// <param name="TestStart">First day of the test window.</param>
/// <param name="TestEnd">Last day of the test window.</param>
/// <param name="TrainCutoff">The last entry date a train trade may have (the embargo); null when the calendar has fewer sessions than the embargo before the window.</param>
/// <param name="TrainTrades">Trades in the train set, over all candidates.</param>
/// <param name="TestTrades">Trades in the test window, over all candidates.</param>
/// <param name="Embargoed">Trades that entered inside the embargo before the window, over all candidates.</param>
/// <param name="Purged">Trades past the embargo that were still open at the window start, over all candidates.</param>
/// <param name="Winner">The train-time winner, or null for "no choice".</param>
/// <param name="OnPlateau">The winner is on a plateau.</param>
/// <param name="Isolated">The winner has no neighbours.</param>
/// <param name="LonePeak">No candidate was on a plateau; the highest score won.</param>
/// <param name="WinnerTrainScore">Train score of the winner.</param>
/// <param name="WinnerTestTrades">Test trades of the winner.</param>
/// <param name="WinnerTestScore">Test score of the winner.</param>
/// <param name="Thin">The winner has fewer than the minimum of trades in the test window, or there is no winner: the fold is out of the stability fraction.</param>
/// <param name="Rows">One row per candidate in the order of the sorted ids.</param>
public sealed record SelectionFold(
    int Index,
    DateOnly TestStart,
    DateOnly TestEnd,
    DateOnly? TrainCutoff,
    int TrainTrades,
    int TestTrades,
    int Embargoed,
    int Purged,
    string? Winner,
    bool OnPlateau,
    bool Isolated,
    bool LonePeak,
    double? WinnerTrainScore,
    int WinnerTestTrades,
    double? WinnerTestScore,
    bool Thin,
    IReadOnlyList<FoldCandidateRow> Rows);

/// <summary>A pooled out-of-sample figure over the test windows of the folds that had a winner (D6).</summary>
/// <param name="Name">The walk-forward choice, <c>default</c> or a candidate id.</param>
/// <param name="Trades">Trades pooled.</param>
/// <param name="MeanR">Mean R.</param>
/// <param name="Score">Score.</param>
public sealed record PooledFigure(string Name, int Trades, double? MeanR, double? Score);

/// <summary>The verdict on the stability of the choice across folds (D8).</summary>
/// <param name="Stable">True, false, or null when there is no statement (fewer than 2 usable folds).</param>
/// <param name="Reason">Why, in one sentence.</param>
/// <param name="Folds">Non-thin folds with a winner.</param>
/// <param name="Modal">The most frequent winner (ties by id).</param>
/// <param name="Agreeing">Folds whose winner is the modal one or its neighbour.</param>
/// <param name="PositiveOutOfSample">Agreeing folds whose winner scored above 0 in its test window.</param>
/// <param name="ChancePass">Probability that uniform random winners satisfy the agreement rule; null when not computed.</param>
/// <param name="LonePeaks">Folds whose winner was a lone peak.</param>
public sealed record SelectionStability(bool? Stable, string Reason, int Folds, string? Modal, int Agreeing, int PositiveOutOfSample, double? ChancePass, int LonePeaks);

/// <summary>
/// The result of the exit choice for one cell (P4-06, D9), schema 1: the inputs, the folds, the final in-sample choice or "no choice", the pooled out-of-sample figures and the verdict. A pure function of its input.
/// </summary>
/// <param name="Input">What it was computed from, without the trades.</param>
/// <param name="Default">The default exit, the first candidate.</param>
/// <param name="FirstEntry">First entry date.</param>
/// <param name="LastEntry">Last entry date.</param>
/// <param name="DroppedWindows">Short final windows dropped.</param>
/// <param name="Folds">The folds.</param>
/// <param name="FinalCandidate">The full-sample winner, in-sample, or null.</param>
/// <param name="FinalPlateau">The full-sample winner is on a plateau.</param>
/// <param name="FinalLonePeak">The full-sample winner is a lone peak.</param>
/// <param name="Chosen">The exit to use: the final choice, or the default for "no choice".</param>
/// <param name="NoChoiceReason">Why there is no choice; null when there is one.</param>
/// <param name="Pooled">The pooled figures.</param>
/// <param name="Stability">The verdict.</param>
/// <param name="Banner">The lines every presentation of the record carries.</param>
public sealed record ExitSelection(
    SelectionInput Input,
    string Default,
    DateOnly? FirstEntry,
    DateOnly? LastEntry,
    int DroppedWindows,
    IReadOnlyList<SelectionFold> Folds,
    string? FinalCandidate,
    bool FinalPlateau,
    bool FinalLonePeak,
    string Chosen,
    string? NoChoiceReason,
    IReadOnlyList<PooledFigure> Pooled,
    SelectionStability Stability,
    IReadOnlyList<string> Banner)
{
    /// <summary>The schema version of the record.</summary>
    public const int Schema = 1;

    /// <summary>The line that says what the choice is and is not (P4-00 criterion 9).</summary>
    public const string InSampleLine = "the choice is in-sample on the data shown (about 2.1 years, no hold-out) and is not evidence of an edge";
}
