namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The hold-out reading of a kept extension (P13-05).</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Rows">Hold-out rows.</param>
/// <param name="DeltaR">Mean paired difference in R per candidate, or null.</param>
/// <param name="LogLossA">A's log loss on the hold-out, or null.</param>
/// <param name="LogLossB">B's log loss on the hold-out, or null.</param>
/// <param name="ThresholdA">A's threshold from all development out-of-fold scores, or null.</param>
/// <param name="ThresholdB">B's threshold, or null.</param>
/// <param name="Note">Why the hold-out was not evaluable, or null.</param>
public sealed record ExtensionHoldOutResult(ExtensionHoldOutStatus Status, int Rows, double? DeltaR, double? LogLossA, double? LogLossB, double? ThresholdA, double? ThresholdB, string? Note);
