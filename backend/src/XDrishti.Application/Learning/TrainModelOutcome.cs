namespace XDrishti.Application.Learning;

/// <summary>What a model registration did.</summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="Sha256">The hash of the model file.</param>
/// <param name="AlreadyRegistered">The same registration was there; nothing was written.</param>
/// <param name="TrainingSetId">The training set the model was made from.</param>
/// <param name="Verdict">Eligible, CannotBeatBaselines or Inconclusive.</param>
/// <param name="Reasons">Why it is not eligible; empty when eligible.</param>
/// <param name="ReportText">The validation report with its honest limits.</param>
/// <param name="Trainer">FastTree or LightGbm.</param>
/// <param name="Note">What a reader must know about the fit, or null.</param>
/// <param name="TrainRows">Development rows the model was trained on.</param>
/// <param name="HoldOutStart">First day of the hold-out that was not read, or null when none was kept.</param>
public sealed record TrainModelOutcome(long ModelId, string Sha256, bool AlreadyRegistered, string TrainingSetId, string Verdict, IReadOnlyList<string> Reasons, string ReportText, string Trainer, string? Note, int TrainRows, DateOnly? HoldOutStart);
