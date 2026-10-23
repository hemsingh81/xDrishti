using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>A registered model as the API shows it (P6-07, ADR 0070): what made it, what the harness said about it and its current role. No model bytes.</summary>
/// <param name="Id">The registry id.</param>
/// <param name="Sha256">The hash of the model file.</param>
/// <param name="CreatedAt">When it was registered.</param>
/// <param name="CreatedBy">Who registered it.</param>
/// <param name="TrainingSetId">The training set it was made from.</param>
/// <param name="FeatureVersion">The feature catalog version.</param>
/// <param name="Trainer">FastTree or LightGbm.</param>
/// <param name="Note">What a reader must know about the fit, or null.</param>
/// <param name="FitRows">Rows the trees were fitted on.</param>
/// <param name="CalibrationRows">Rows the calibration was fitted on.</param>
/// <param name="TrainRows">Development rows it was trained on.</param>
/// <param name="From">Earliest trade date among them.</param>
/// <param name="To">Latest trade date among them.</param>
/// <param name="HoldOutStart">First day of the hold-out that was not read, or null when none was kept.</param>
/// <param name="Verdict">Eligible, CannotBeatBaselines or Inconclusive.</param>
/// <param name="UsedFolds">Folds used.</param>
/// <param name="FoldsBeatingBoth">Folds in which it beat both baselines.</param>
/// <param name="FoldsNeeded">Folds it had to win.</param>
/// <param name="PooledBeatsBoth">It beat both baselines pooled.</param>
/// <param name="PooledEcePp">Pooled expected calibration error in percentage points.</param>
/// <param name="PooledLogLoss">Pooled out-of-sample log loss.</param>
/// <param name="PooledBrier">Pooled out-of-sample Brier score.</param>
/// <param name="Reasons">Why it is not eligible; empty when eligible.</param>
/// <param name="Role">The current role: challenger, champion or retired.</param>
/// <param name="MayBeChampion">The record allows the role champion: an Eligible verdict, a hold-out kept, enough folds and the trials of the registry.</param>
public sealed record ModelView(
    long Id,
    string Sha256,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    string TrainingSetId,
    string FeatureVersion,
    string Trainer,
    string? Note,
    int FitRows,
    int CalibrationRows,
    int TrainRows,
    DateOnly From,
    DateOnly To,
    DateOnly? HoldOutStart,
    string Verdict,
    int UsedFolds,
    int FoldsBeatingBoth,
    int FoldsNeeded,
    bool PooledBeatsBoth,
    double PooledEcePp,
    double PooledLogLoss,
    double PooledBrier,
    IReadOnlyList<string> Reasons,
    string Role,
    bool MayBeChampion)
{
    /// <summary>The view of a stored model.</summary>
    internal static ModelView Of(StoredModel stored)
    {
        var m = stored.Model;
        return new ModelView(
            stored.Id, m.Sha256, m.CreatedAt, m.CreatedBy, m.TrainingSetId, m.FeatureVersion, m.Trainer, m.Note, m.FitRows, m.CalibrationRows, m.TrainRows, m.FromDate, m.ToDate, m.HoldOutStart, m.Verdict.ToString(), m.UsedFolds,
            m.FoldsBeatingBoth, m.FoldsNeeded, m.PooledBeatsBoth, m.PooledEcePp, m.PooledLogLoss, m.PooledBrier, m.Reasons.Length == 0 ? [] : m.Reasons.Split('\n'), stored.Role.Code(), ChampionRule.MayBeChampion(m.Verdict, m.HoldOutStart, m.UsedFolds, m.Trials, stored.RequiredTrials));
    }
}
