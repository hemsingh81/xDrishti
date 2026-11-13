namespace XDrishti.Application.Learning;

/// <summary>One reason a term of the influence predicate does not hold.</summary>
/// <param name="Code">A stable code such as <c>holdout_reused</c>.</param>
/// <param name="Text">A sentence.</param>
public sealed record MlReasonView(string Code, string Text);

/// <summary>One term of the influence predicate.</summary>
/// <param name="Name">The term (HoldOutBeatsNoMl, ForwardShadowOk, NoCalibrationAlarm, SwitchOn).</param>
/// <param name="State"><c>holds</c>, <c>fails</c> or <c>unavailable</c> (an unavailable figure is not a pass).</param>
/// <param name="Reasons">Why not; empty when it holds.</param>
public sealed record MlTermView(string Name, string State, IReadOnlyList<MlReasonView> Reasons);

/// <summary>Where the model stands with the locked hold-out.</summary>
/// <param name="State"><c>not_kept</c>, <c>kept</c>, <c>used</c> or <c>reused</c>.</param>
/// <param name="Family">The model family whose one look it is.</param>
/// <param name="BlockStart">The start of the block the look was taken on, or null before a look.</param>
/// <param name="Rows">Hold-out rows the model was scored on, or null when not available.</param>
/// <param name="ProbabilityBeatsNoMl">The probability of beating the best no-ML baseline there, or null when not available.</param>
/// <param name="PolicyEffectR">The effect of the take rule on realised R there, or null when not available.</param>
/// <param name="LogLossModel">The model's mean log loss there (CF-10e part 3), or null before the figures were stored.</param>
/// <param name="LogLossBaseline">The better no-ML baseline's mean log loss there, or null.</param>
/// <param name="BaselineName"><c>base_rate</c> or <c>cell_rate</c>, or null.</param>
/// <param name="GainLow">The lower end of the interval of the mean log-loss gain over the baseline, or null.</param>
/// <param name="GainHigh">The upper end, or null.</param>
/// <param name="Mode"><c>owner</c> or <c>auto</c> (how the look was taken), or null.</param>
public sealed record MlHoldOutView(string State, string Family, DateOnly? BlockStart, int? Rows, double? ProbabilityBeatsNoMl, double? PolicyEffectR, double? LogLossModel = null, double? LogLossBaseline = null, string? BaselineName = null, double? GainLow = null, double? GainHigh = null, string? Mode = null);

/// <summary><c>GET /api/learning/models/{id}/influence</c>: whether the model may influence a decision, term by term (CF-10 part 10c-2, ADR 0217).</summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="Role">The model's current role.</param>
/// <param name="MayInfluence">True only when every term holds and the model is the current champion; false by default.</param>
/// <param name="Terms">The four terms.</param>
/// <param name="Reasons">Every reason of every term that does not hold, plus <c>not_champion</c>; empty only when allowed.</param>
/// <param name="HoldOut">The hold-out state.</param>
/// <param name="SwitchOn">The owner's switch ml.influence.enabled.</param>
/// <param name="SettingHash">SHA-256 of the thresholds and the switch.</param>
/// <param name="Settings">The thresholds as text.</param>
/// <param name="Notes">What a reader must know about the figures that are not available.</param>
public sealed record MlInfluenceView(
    long ModelId,
    string Role,
    bool MayInfluence,
    IReadOnlyList<MlTermView> Terms,
    IReadOnlyList<MlReasonView> Reasons,
    MlHoldOutView HoldOut,
    bool SwitchOn,
    string SettingHash,
    string Settings,
    IReadOnlyList<string> Notes);
