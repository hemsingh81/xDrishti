namespace XDrishti.Domain.Learning;

/// <summary>
/// The outcome for one cell (P6-14): the proposed window with its evidence, or the honest reason there is none. It is a hypothesis; nothing here changes a strategy or the scanner.
/// </summary>
/// <param name="CellKey">The cell as text.</param>
/// <param name="Status">The honest outcome.</param>
/// <param name="CellTrades">Cell trades inside the regular session.</param>
/// <param name="CellWinRate">Their win rate (the prior centre); null with no trades.</param>
/// <param name="WindowStartMinute">Start of the best window found on all the data (even when not stable); null when none.</param>
/// <param name="WindowEndMinute">End (exclusive).</param>
/// <param name="WindowTrades">Trades in the window.</param>
/// <param name="WindowWins">Of them with label 1.</param>
/// <param name="ShrunkRate">The window's shrunk win rate.</param>
/// <param name="Lift">Shrunk rate minus the cell win rate.</param>
/// <param name="MeanRInside">Mean R inside the window.</param>
/// <param name="MeanROutside">Mean R of the cell's other trades; null when none.</param>
/// <param name="KeptShare">The share of the cell's trades inside the window.</param>
/// <param name="RemovesMajority">The window would drop more than half the cell's trades (doc 09 honesty rule): flagged.</param>
/// <param name="FoldsEvaluable">Folds that could judge the window learnt on their train rows.</param>
/// <param name="FoldsHeld">Of them where it beat the outside.</param>
/// <param name="FoldsRequired">Folds that had to hold (the share of the validation options, rounded up).</param>
/// <param name="PooledLift">Out-of-sample win rate inside minus outside, pooled over the evaluable folds; null when none.</param>
/// <param name="FoldsOverlapFinal">Fold windows that overlap the final window.</param>
/// <param name="FoldsEqualFinal">Fold windows equal to the final window.</param>
/// <param name="Folds">Every shared fold for this cell.</param>
/// <param name="Buckets">Every bucket of the session for this cell.</param>
/// <param name="Reasons">Why the status is what it is, one sentence each.</param>
public sealed record EntryWindowProposal(
    string CellKey,
    EntryWindowStatus Status,
    int CellTrades,
    double? CellWinRate,
    int? WindowStartMinute,
    int? WindowEndMinute,
    int WindowTrades,
    int WindowWins,
    double? ShrunkRate,
    double? Lift,
    double? MeanRInside,
    double? MeanROutside,
    double KeptShare,
    bool RemovesMajority,
    int FoldsEvaluable,
    int FoldsHeld,
    int FoldsRequired,
    double? PooledLift,
    int FoldsOverlapFinal,
    int FoldsEqualFinal,
    IReadOnlyList<EntryWindowFold> Folds,
    IReadOnlyList<EntryWindowBucket> Buckets,
    IReadOnlyList<string> Reasons);
