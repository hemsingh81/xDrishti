namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The stored figures of a model's one look at a hold-out block (<c>research.ml_holdout_scores</c>, CF-10e part 3, ADR 0285).</summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="BlockStart">The first day of the block the look was taken on.</param>
/// <param name="LookKey">The key of the look row in <c>lab.holdout_unlocks</c>.</param>
/// <param name="Rows">Scored rows.</param>
/// <param name="Days">Distinct trade days.</param>
/// <param name="LogLossModel">The model's mean log loss.</param>
/// <param name="LogLossBaseline">The better no-ML baseline's mean log loss.</param>
/// <param name="BaselineName"><c>base_rate</c> or <c>cell_rate</c>.</param>
/// <param name="ProbabilityBetter">The bootstrap probability of beating the baseline, or null when it could not be assessed.</param>
/// <param name="GainLow">The lower end of the interval of the mean log-loss gain, or null.</param>
/// <param name="GainHigh">The upper end, or null.</param>
/// <param name="PolicyEffectR">The effect of the fixed take rule on realised R per candidate.</param>
/// <param name="InputHash">SHA-256 of the scored rows (what the figures were made of).</param>
/// <param name="SettingHash">SHA-256 of the thresholds and the setting at the time.</param>
/// <param name="Mode"><c>owner</c> or <c>auto</c>.</param>
/// <param name="Actor">The owner, or <c>system:auto-score</c>.</param>
/// <param name="ScoredAt">When (the database stamps it on insert).</param>
public sealed record StoredMlHoldOutScore(
    long ModelId,
    DateOnly BlockStart,
    string LookKey,
    int Rows,
    int Days,
    double LogLossModel,
    double LogLossBaseline,
    string BaselineName,
    double? ProbabilityBetter,
    double? GainLow,
    double? GainHigh,
    double PolicyEffectR,
    string InputHash,
    string SettingHash,
    string Mode,
    string Actor,
    DateTimeOffset ScoredAt);

/// <summary>Where the figures of the model hold-out looks are kept. Insert-only; one row per model and block.</summary>
public interface IMlHoldOutScores
{
    /// <summary>The figures of the model's look at the block that starts on this date, or null when none was stored (a look that was claimed and abandoned has none).</summary>
    ValueTask<StoredMlHoldOutScore?> FindAsync(long modelId, DateOnly blockStart, CancellationToken cancellationToken);

    /// <summary>Stores the figures; false (nothing written) when the model has a row at the block already.</summary>
    ValueTask<bool> InsertAsync(StoredMlHoldOutScore score, CancellationToken cancellationToken);
}
