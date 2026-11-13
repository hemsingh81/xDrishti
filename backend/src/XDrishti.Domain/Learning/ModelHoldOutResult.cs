namespace XDrishti.Domain.Learning;

/// <summary>The figures of a model's one look at the hold-out (CF-10e part 3, ADR 0285). A bootstrap figure that cannot be assessed (too few trade days) is null, which the predicate counts as unavailable, never as a pass.</summary>
/// <param name="Rows">Scored rows.</param>
/// <param name="Days">Distinct trade days.</param>
/// <param name="LogLossModel">The model's mean log loss.</param>
/// <param name="LogLossBaseline">The mean log loss of the better no-ML baseline.</param>
/// <param name="BaselineName"><c>base_rate</c> or <c>cell_rate</c>.</param>
/// <param name="ProbabilityBetter">The paired day-cluster bootstrap probability that the model beats that baseline, or null.</param>
/// <param name="GainLow">The lower end of the interval of the mean log-loss gain, or null.</param>
/// <param name="GainHigh">The upper end, or null.</param>
/// <param name="PolicyEffectR">The effect of the fixed take rule on realised R per candidate (kept R minus take-all R, per candidate).</param>
public sealed record ModelHoldOutResult(int Rows, int Days, double LogLossModel, double LogLossBaseline, string BaselineName, double? ProbabilityBetter, double? GainLow, double? GainHigh, double PolicyEffectR);
