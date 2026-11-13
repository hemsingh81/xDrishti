namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The words of how a model's look at a hold-out block ended (<c>research.ml_holdout_scores.outcome</c>).</summary>
public static class MlHoldOutOutcome
{
    /// <summary>The figures were stored.</summary>
    public const string Scored = "scored";

    /// <summary>The block gave no candidate the model could score: terminal, no figures.</summary>
    public const string NoRows = "no_rows";

    /// <summary>The pass failed in a way that does not go away (or kept failing past the resume window): terminal, no figures, the code says why.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// How a model's one look at a hold-out block ended (<c>research.ml_holdout_scores</c>, CF-10e part 3, ADR 0285): the stored figures, or a terminal outcome with no figures. A terminal row ends the look: the gate reads it as unavailable (never a pass) and the release may go on. The row belongs to the look
/// (look key, block and look id), not to a registry row: two registry rows with the same file share the one look.
/// </summary>
/// <param name="ModelId">The registry id of the model that finished the look.</param>
/// <param name="BlockStart">The first day of the block the look was taken on.</param>
/// <param name="LookKey">The key of the look row in <c>lab.holdout_unlocks</c>.</param>
/// <param name="Outcome"><see cref="MlHoldOutOutcome"/>.</param>
/// <param name="OutcomeCode">Why a terminal outcome ended the look; null when scored.</param>
/// <param name="Rows">Scored rows (null when not scored).</param>
/// <param name="Days">Distinct trade days.</param>
/// <param name="LogLossModel">The model's mean log loss.</param>
/// <param name="LogLossBaseline">The better no-ML baseline's mean log loss.</param>
/// <param name="BaselineName"><c>base_rate</c> or <c>cell_rate</c>.</param>
/// <param name="ProbabilityBetter">The bootstrap probability of beating the baseline, or null when it could not be assessed.</param>
/// <param name="GainLow">The lower end of the interval of the mean log-loss gain, or null.</param>
/// <param name="GainHigh">The upper end, or null.</param>
/// <param name="PolicyEffectR">The effect of the fixed take rule on realised R per candidate.</param>
/// <param name="InputHash">SHA-256 of the scored rows (what the figures were made of), or of the terminal outcome.</param>
/// <param name="SettingHash">SHA-256 of the thresholds and the setting at the time.</param>
/// <param name="Mode"><c>owner</c> or <c>auto</c>.</param>
/// <param name="Actor">The owner, or <c>system:auto-score</c>.</param>
/// <param name="ScoredAt">When (the database stamps it on insert).</param>
/// <param name="Dropped">Candidates of the block that could not be scored (no features or cell statistics).</param>
/// <param name="BlockEnd">The last day of the block.</param>
/// <param name="LookId">The id of the look (the model's stable look id, kept in the unlock row).</param>
/// <param name="SettingId">The row of <c>ml.holdout.auto_score</c> in force when the look ended, or null when none.</param>
/// <param name="VersionsPlanned">Strategy versions the pass planned (those of the training job that finished a run).</param>
/// <param name="VersionsRun">Strategy versions that ran over the block; a scored row has all of them.</param>
public sealed record StoredMlHoldOutScore(
    long ModelId,
    DateOnly BlockStart,
    string LookKey,
    string Outcome,
    string? OutcomeCode,
    int? Rows,
    int? Days,
    double? LogLossModel,
    double? LogLossBaseline,
    string? BaselineName,
    double? ProbabilityBetter,
    double? GainLow,
    double? GainHigh,
    double? PolicyEffectR,
    string InputHash,
    string SettingHash,
    string Mode,
    string Actor,
    DateTimeOffset ScoredAt,
    int Dropped,
    DateOnly BlockEnd,
    Guid LookId,
    long? SettingId,
    int VersionsPlanned,
    int VersionsRun)
{
    /// <summary>Whether the figures were stored (a terminal outcome has none).</summary>
    public bool IsScored => string.Equals(Outcome, MlHoldOutOutcome.Scored, StringComparison.Ordinal);
}

/// <summary>Where the figures and terminal outcomes of the model hold-out looks are kept. Insert-only; one row per look.</summary>
public interface IMlHoldOutScores
{
    /// <summary>How the look ended (its key, block and id), or null while it has not ended (a look that was claimed and is still open has no row).</summary>
    ValueTask<StoredMlHoldOutScore?> FindByLookAsync(string lookKey, DateOnly blockStart, Guid lookId, CancellationToken cancellationToken);

    /// <summary>Stores the row; false (nothing written) when the model or the look has a row at the block already.</summary>
    ValueTask<bool> InsertAsync(StoredMlHoldOutScore score, CancellationToken cancellationToken);
}
