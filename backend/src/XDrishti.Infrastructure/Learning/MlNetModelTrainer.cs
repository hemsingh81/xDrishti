using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Learning.MlNet;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// The model trainer port over the ML.NET adapter (spec P6-07, ADR 0070). The preferred trainer is <b>FastTree</b>: the container images carry no <c>libgomp1</c>, so LightGBM is not proven there (ADR 0068, decision 3); switching is this one setting after a container build
/// shows LightGBM training. The candidate of the harness and the final model use the same settings, so the verdict is about the model that is stored.
/// </summary>
internal sealed class MlNetModelTrainer : IModelTrainer
{
    private readonly MlNetTrainerOptions _settings;
    private readonly MlNetScorerFactory _factory;

    /// <summary>The trainer with the calibration cut of <c>Confidence:Calibration</c> (CF-10 part 10a, ADR 0214).</summary>
    public MlNetModelTrainer(CalibrationOptions calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        _settings = new MlNetTrainerOptions(Preferred: TreeTrainer.FastTree, IsotonicMinRows: calibration.IsotonicMinRows);
        _factory = new MlNetScorerFactory(_settings);
        Parameters = string.Create(CultureInfo.InvariantCulture,
            $"preferred={_settings.Preferred} trees={_settings.Trees} leaves={_settings.Leaves} learning_rate={_settings.LearningRate:R} min_leaf_rows={_settings.MinLeafRows} seed={_settings.Seed} calibration_share={_settings.CalibrationShare:R} min_fit_rows={_settings.MinFitRows} min_calibration_rows={_settings.MinCalibrationRows} probability_floor={_settings.ProbabilityFloor:R} isotonic_min_rows={_settings.IsotonicMinRows}");
    }

    public IScorerFactory Candidate => _factory;

    public string Parameters { get; }

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
