using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>
/// A trained model and what made and judged it, as the registry keeps it (P6-07, ADR 0070). The model bytes are not here: they are a file named by <see cref="Sha256"/>. Everything is fixed at registration; only designations are added later.
/// </summary>
/// <param name="VersionKey">SHA-256 of the model hash, the training-set id, the parameters and the report text: the idempotency key of the registration.</param>
/// <param name="Sha256">The hash of the model file (lower-case hex).</param>
/// <param name="ArtifactBytes">The file size.</param>
/// <param name="Trainer">The tree trainer that was used (FastTree or LightGbm).</param>
/// <param name="Note">What a reader must know about the fit (a fall-back, for example), or null.</param>
/// <param name="TrainingSetId">The id of the stored training set the model was made from.</param>
/// <param name="FeatureVersion">The feature catalog version; the feature order of a score.</param>
/// <param name="FeatureWidth">Number of features.</param>
/// <param name="FitRows">Rows the trees were fitted on.</param>
/// <param name="CalibrationRows">Rows the calibration map was fitted on.</param>
/// <param name="TrainRows">Development rows the final model was trained on.</param>
/// <param name="FromDate">Earliest trade date among them.</param>
/// <param name="ToDate">Latest trade date among them.</param>
/// <param name="HoldOutStart">First day of the hold-out that was not read, or null when none was kept.</param>
/// <param name="Verdict">The validation verdict of the harness (P6-05).</param>
/// <param name="UsedFolds">Folds used.</param>
/// <param name="FoldsBeatingBoth">Folds in which it beat both baselines.</param>
/// <param name="FoldsNeeded">Folds it had to win.</param>
/// <param name="PooledBeatsBoth">It beat both baselines pooled.</param>
/// <param name="PooledEcePp">Pooled expected calibration error, in percentage points.</param>
/// <param name="PooledLogLoss">Pooled out-of-sample log loss.</param>
/// <param name="PooledBrier">Pooled out-of-sample Brier score.</param>
/// <param name="Reasons">Why it is not eligible, one per line; empty when eligible.</param>
/// <param name="ReportText">The full validation report as text, with its honest limits.</param>
/// <param name="Parameters">The trainer and validation settings as text.</param>
/// <param name="CalibrationScores">The calibration table, raw scores.</param>
/// <param name="CalibrationValues">The calibration table, probabilities.</param>
/// <param name="Importance">The mean absolute contribution per feature, in feature order.</param>
/// <param name="Trials">The trials the verdict was widened for: the larger of the command line and the models of this feature version the registry already had, plus this one.</param>
/// <param name="PurgedRows">Development rows dropped because their label horizon reaches into the hold-out (0 when none was kept).</param>
/// <param name="CreatedAt">When.</param>
/// <param name="CreatedBy">Who.</param>
public sealed record ModelToRegister(
    string VersionKey,
    string Sha256,
    int ArtifactBytes,
    string Trainer,
    string? Note,
    string TrainingSetId,
    string FeatureVersion,
    int FeatureWidth,
    int FitRows,
    int CalibrationRows,
    int TrainRows,
    DateOnly FromDate,
    DateOnly ToDate,
    DateOnly? HoldOutStart,
    ValidationOutcome Verdict,
    int UsedFolds,
    int FoldsBeatingBoth,
    int FoldsNeeded,
    bool PooledBeatsBoth,
    double PooledEcePp,
    double PooledLogLoss,
    double PooledBrier,
    string Reasons,
    string ReportText,
    string Parameters,
    IReadOnlyList<double> CalibrationScores,
    IReadOnlyList<double> CalibrationValues,
    IReadOnlyList<double> Importance,
    int Trials,
    int PurgedRows,
    DateTimeOffset CreatedAt,
    string CreatedBy);
