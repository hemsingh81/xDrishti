using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Learning.MlNet;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// The model trainer port over the ML.NET adapter (spec P6-07, ADR 0070). The preferred trainer is <b>FastTree</b>: the container images carry no <c>libgomp1</c>, so LightGBM is not proven there (ADR 0068, decision 3); switching is this one setting after a container build
/// shows LightGBM training. The candidate of the harness and the final model use the same settings, so the verdict is about the model that is stored.
/// </summary>
internal sealed class MlNetModelTrainer : IModelTrainer
{
    private static readonly MlNetTrainerOptions Settings = new(Preferred: TreeTrainer.FastTree);

    private readonly MlNetScorerFactory _factory = new(Settings);

    public IScorerFactory Candidate => _factory;

    public string Parameters { get; } = string.Create(CultureInfo.InvariantCulture,
        $"preferred={Settings.Preferred} trees={Settings.Trees} leaves={Settings.Leaves} learning_rate={Settings.LearningRate:R} min_leaf_rows={Settings.MinLeafRows} seed={Settings.Seed} calibration_share={Settings.CalibrationShare:R} min_fit_rows={Settings.MinFitRows} min_calibration_rows={Settings.MinCalibrationRows} probability_floor={Settings.ProbabilityFloor:R}");

    public Result<TrainedModel> Train(IReadOnlyList<ValidationRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var trained = _factory.Train(rows);
        if (trained.IsFailure)
        {
            return Result.Failure<TrainedModel>(trained.Error);
        }

        var model = trained.Value;
        var artifact = model.ToArtifact();
        return Result.Success(new TrainedModel(artifact.Bytes, artifact.Sha256, model.Trainer.ToString(), model.Note, model.Width, model.FitRows, model.CalibrationRows, [.. model.Calibration.Scores], [.. model.Calibration.Values], [.. model.Importance(rows)]));
    }
}
