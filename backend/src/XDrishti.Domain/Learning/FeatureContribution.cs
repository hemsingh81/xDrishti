namespace XDrishti.Domain.Learning;

/// <summary>One feature's share of one prediction (P6-06): the feature's position in the feature array and its signed contribution to the raw score (the margin before calibration); positive pushes towards label 1.</summary>
/// <param name="Index">The position in <see cref="ValidationRow.Features"/>.</param>
/// <param name="Value">The signed contribution.</param>
public sealed record FeatureContribution(int Index, double Value);
