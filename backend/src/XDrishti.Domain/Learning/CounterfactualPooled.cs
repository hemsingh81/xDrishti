namespace XDrishti.Domain.Learning;

/// <summary>Both sides on the same rows, pooled over some windows (P6-12), with the paired log loss gain of retraining (frozen minus retrained, per row; positive means retraining was better).</summary>
/// <param name="Rows">Rows scored by both sides.</param>
/// <param name="Frozen">The frozen side.</param>
/// <param name="Retrained">The retrained side.</param>
/// <param name="MeanGain">Mean paired log loss gain.</param>
/// <param name="GainStandardError">Standard error of the mean gain with the rows clustered by trade date (trades of one day share the market, so they are not independent); null with fewer than two days.</param>
public sealed record CounterfactualPooled(int Rows, CounterfactualSide Frozen, CounterfactualSide Retrained, double MeanGain, double? GainStandardError);
