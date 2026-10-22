namespace XDrishti.Domain.Learning;

/// <summary>The verdict on one candidate (P6-05).</summary>
/// <param name="Name">The candidate.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="FoldsBeatingBoth">Used folds in which it beat the cell-rate and the logistic baseline on log loss and Brier by the margin.</param>
/// <param name="UsedFolds">Used folds.</param>
/// <param name="FoldsNeeded">The folds it had to win.</param>
/// <param name="PooledBeatsBoth">It beat both baselines by the margin on the pooled out-of-sample rows.</param>
/// <param name="PooledEcePp">The pooled expected calibration error in pp.</param>
/// <param name="Reasons">Why it is not eligible or only inconclusive; empty when eligible.</param>
public sealed record CandidateVerdict(string Name, ValidationOutcome Outcome, int FoldsBeatingBoth, int UsedFolds, int FoldsNeeded, bool PooledBeatsBoth, double PooledEcePp, IReadOnlyList<string> Reasons);
