namespace XDrishti.Application.Abstractions.Research;

/// <summary>One candidate in one fold of a stored exit choice (P4-06, D10).</summary>
/// <param name="FoldIndex">1-based fold number.</param>
/// <param name="TestStart">First day of the test window.</param>
/// <param name="TestEnd">Last day of the test window.</param>
/// <param name="CandidateId">The candidate id.</param>
/// <param name="Qualified">It had the minimum of trades in the train set.</param>
/// <param name="TrainTrades">Trades in the train set.</param>
/// <param name="TrainScore">Score on the train set.</param>
/// <param name="TestTrades">Trades in the test window.</param>
/// <param name="TestScore">Score on the test window.</param>
/// <param name="IsWinner">It won the fold.</param>
public sealed record ExitSelectionFoldToStore(int FoldIndex, DateOnly TestStart, DateOnly TestEnd, string CandidateId, bool Qualified, int TrainTrades, double? TrainScore, int TestTrades, double? TestScore, bool IsWinner);

/// <summary>
/// What the store is given for an exit choice (P4-06, D10): the key, the stored comparison it was made from, the cell, the result, the record text with its digest and one row for each fold and candidate. The text and its digest come from the caller,
/// who made them from the record it printed.
/// </summary>
/// <param name="SelectionKey">The idempotency key.</param>
/// <param name="ComparisonKey">The key of the stored comparison the choice was made from.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="Timeframe">The timeframe code.</param>
/// <param name="Side">The side(s) of the cell.</param>
/// <param name="Style">The style of the cell (intraday or swing).</param>
/// <param name="Regime">The regime; <c>*</c> until P6.</param>
/// <param name="ChosenCandidateId">The final choice, or null for "no choice".</param>
/// <param name="Stable">The verdict, or null for "no statement".</param>
/// <param name="RecordJson">The record as <c>exit-selection.json</c>.</param>
/// <param name="RecordSha256">SHA-256 of the UTF-8 bytes of <paramref name="RecordJson"/>.</param>
/// <param name="Folds">One row for each fold and candidate.</param>
/// <param name="Actor">Who stores it.</param>
/// <param name="CreatedAt">When (read once by the handler from <c>TimeProvider</c>).</param>
public sealed record ExitSelectionToStore(
    string SelectionKey,
    string ComparisonKey,
    string StrategyId,
    string Timeframe,
    string Side,
    string Style,
    string Regime,
    string? ChosenCandidateId,
    bool? Stable,
    string RecordJson,
    string RecordSha256,
    IReadOnlyList<ExitSelectionFoldToStore> Folds,
    string Actor,
    DateTimeOffset CreatedAt);
