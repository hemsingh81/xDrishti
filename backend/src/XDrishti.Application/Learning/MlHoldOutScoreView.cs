namespace XDrishti.Application.Learning;

/// <summary><c>POST /api/learning/models/{id}/holdout-score</c>: the figures of the model's one look at the locked hold-out (CF-10e part 3, ADR 0285).</summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="Family">The model family whose one look at the block it is.</param>
/// <param name="BlockStart">The first day of the block.</param>
/// <param name="Mode"><c>owner</c> or <c>auto</c>.</param>
/// <param name="Actor">The owner, or <c>system:auto-score</c>.</param>
/// <param name="Rows">Candidates the model scored.</param>
/// <param name="Days">Distinct trade days.</param>
/// <param name="Dropped">Candidates that could not be scored (no features or no cell statistics); never imputed.</param>
/// <param name="LogLossModel">The model's mean log loss.</param>
/// <param name="LogLossBaseline">The better no-ML baseline's mean log loss.</param>
/// <param name="BaselineName"><c>base_rate</c> or <c>cell_rate</c>.</param>
/// <param name="ProbabilityBeatsNoMl">The paired bootstrap probability that the model beats that baseline, or null when it could not be assessed (not a pass).</param>
/// <param name="GainLow">The lower end of the interval of the mean log-loss gain, or null.</param>
/// <param name="GainHigh">The upper end, or null.</param>
/// <param name="PolicyEffectR">The effect of the fixed take rule on realised R per candidate.</param>
/// <param name="SettingHash">SHA-256 of the thresholds and the setting snapshot.</param>
/// <param name="InputHash">SHA-256 of the scored rows.</param>
/// <param name="ScoredAt">When.</param>
public sealed record MlHoldOutScoreView(
    long ModelId,
    string Family,
    DateOnly BlockStart,
    string Mode,
    string Actor,
    int Rows,
    int Days,
    int Dropped,
    double LogLossModel,
    double LogLossBaseline,
    string BaselineName,
    double? ProbabilityBeatsNoMl,
    double? GainLow,
    double? GainHigh,
    double PolicyEffectR,
    string SettingHash,
    string InputHash,
    DateTimeOffset ScoredAt);
