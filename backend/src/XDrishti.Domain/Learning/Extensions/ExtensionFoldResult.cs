namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One calendar fold of a comparison (P13-05). The gain, thresholds and counts are null where the fold was not used or not evaluable.</summary>
/// <param name="Start">First day of the window.</param>
/// <param name="End">Last day of the window.</param>
/// <param name="TrainRows">Train rows.</param>
/// <param name="TestRows">Test rows.</param>
/// <param name="Used">Whether both models were fitted and scored on the fold.</param>
/// <param name="Evaluable">Whether both models had a threshold from earlier folds.</param>
/// <param name="Note">Why the fold was skipped or not evaluable, or null.</param>
/// <param name="ThresholdA">Model A's take threshold.</param>
/// <param name="ThresholdB">Model B's take threshold.</param>
/// <param name="Candidates">Test rows of an evaluable fold.</param>
/// <param name="Flips">Rows where A and B decide differently.</param>
/// <param name="DeltaR">Mean paired difference in R per candidate.</param>
public sealed record ExtensionFoldResult(DateOnly Start, DateOnly End, int TrainRows, int TestRows, bool Used, bool Evaluable, string? Note, double? ThresholdA, double? ThresholdB, int Candidates, int Flips, double? DeltaR);
