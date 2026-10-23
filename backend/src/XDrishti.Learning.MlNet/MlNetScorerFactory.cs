using System.Globalization;
using Microsoft.ML;
using Microsoft.ML.Calibrators;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;
using Microsoft.ML.Trainers.LightGbm;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using Isotonic = XDrishti.Domain.Learning.IsotonicCalibrator;

namespace XDrishti.Learning.MlNet;

/// <summary>
/// The candidate model of the validation harness (P6-06, ADR 0068): <see cref="IScorerFactory"/> over the train rows of a fold. The train rows are ordered by decision time; the earliest part fits the trees, the latest
/// <see cref="MlNetTrainerOptions.CalibrationShare"/> is held back and only fits the PAV calibration map (fit rows whose label horizon reaches the first held-back decision are dropped, so the map is not fitted on rows the trees were helped by).
/// Test rows never reach <c>Fit</c>. LightGBM is tried first; when its native library cannot load, FastTree is used and the model's note says so. Deterministic: one thread, fixed seed.
/// When the rows cannot be trained on (too few, one class, no features) <see cref="Fit"/> returns the smoothed base rate with a note, and <see cref="Train"/> returns the failure.
/// </summary>
/// <param name="options">The settings; the defaults are the conservative fixed ones.</param>
public sealed class MlNetScorerFactory(MlNetTrainerOptions? options = null) : IScorerFactory
{
    /// <summary>The report name (doc 16 <c>meta_model</c>).</summary>
    public const string ScorerName = "meta_model";

    private readonly MlNetTrainerOptions _options = options ?? new MlNetTrainerOptions();

    /// <inheritdoc />
    public string Name => ScorerName;

    /// <inheritdoc />
    public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
    {
        var result = Train(train);
        return result.IsSuccess ? result.Value : new Untrained(BaseRateScorer.Rate(train), result.Error.Message);
    }

    /// <summary>Trains a model on the rows, or says why it cannot.</summary>
    /// <param name="train">The train rows (never the test rows).</param>
    public Result<MlNetModel> Train(IReadOnlyList<ValidationRow> train)
    {
        ArgumentNullException.ThrowIfNull(train);
        var problems = _options.Problems();
        if (problems.Count > 0)
        {
            return Result.Failure<MlNetModel>(Error.Validation("ml.bad_options", string.Join("; ", problems)));
        }

        var width = train.Count == 0 ? 0 : train[0].Features.Length;
        if (width == 0)
        {
            return Result.Failure<MlNetModel>(Error.Validation("ml.no_features", "The rows carry no features; there is nothing for a model to learn from."));
        }

        if (train.Any(r => r.Features.Length != width))
        {
            return Result.Failure<MlNetModel>(Error.Validation("ml.bad_feature_width", "The rows do not all have the same number of features."));
        }

        var ordered = train.OrderBy(r => r.DecisionEpochSeconds).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var calibrationCount = (int)Math.Ceiling(ordered.Length * _options.CalibrationShare);
        var held = ordered[^calibrationCount..];
        var firstHeld = held[0].DecisionEpochSeconds;
        var fit = ordered[..^calibrationCount].Where(r => r.ResolvedEpochSeconds < firstHeld).ToArray();
        if (fit.Length < _options.MinFitRows || held.Length < _options.MinCalibrationRows)
        {
            return Result.Failure<MlNetModel>(Error.Validation("ml.too_few_rows", string.Create(CultureInfo.InvariantCulture, $"{fit.Length} rows to fit and {held.Length} to calibrate; {_options.MinFitRows} and {_options.MinCalibrationRows} needed.")));
        }

        if (fit.All(r => r.Label == 1) || fit.All(r => r.Label == 0) || held.All(r => r.Label == 1) || held.All(r => r.Label == 0))
        {
            return Result.Failure<MlNetModel>(Error.Validation("ml.one_class", "The fit rows or the calibration rows have a single class."));
        }

        var means = Means(fit, width);
        var ml = new MLContext(seed: _options.Seed);
        var examples = fit.Select(r => new Example { Features = MlNetModel.Impute(r.Features, means), Label = r.Label == 1 });
        var data = Example.View(ml, examples, width);

        var (trees, trainer, note) = FitTrees(ml, data);
        var header = new ArtifactHeader(1, trainer, width, means, [], [], _options.ProbabilityFloor, fit.Length, held.Length, _options.Seed, note);
        var uncalibrated = new MlNetModel(ml, trees, header, Isotonic.FromKnots([0], [0.5]));
        var raw = held.Select(uncalibrated.RawScore).ToArray();
        var calibration = Isotonic.Fit(raw, [.. held.Select(r => r.Label)]);
        header = header with { CalibrationScores = [.. calibration.Scores], CalibrationValues = [.. calibration.Values] };
        return Result.Success(new MlNetModel(ml, trees, header, calibration));
    }

    private static double[] Means(IReadOnlyList<ValidationRow> rows, int width)
    {
        var means = new double[width];
        for (var j = 0; j < width; j++)
        {
            var sum = 0.0;
            var n = 0;
            foreach (var r in rows)
            {
                if (double.IsFinite(r.Features[j]))
                {
                    sum += r.Features[j];
                    n++;
                }
            }

            means[j] = n == 0 ? 0 : sum / n;
        }

        return means;
    }

    private (ITransformer Trees, TreeTrainer Trainer, string? Note) FitTrees(MLContext ml, IDataView data)
    {
        string? note = null;
        if (_options.Preferred == TreeTrainer.LightGbm)
        {
            try
            {
                var lightGbm = FitLightGbm(ml, data);
                return (lightGbm, TreeTrainer.LightGbm, null);
            }
            catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or TypeInitializationException or EntryPointNotFoundException or InvalidOperationException or NotSupportedException or ArgumentException or InvalidProgramException)
            {
                // The native LightGBM library is not loadable on every machine (for example macOS arm64 ships none): use FastTree and say so, so that the numbers are never read as LightGBM's.
                note = string.Create(CultureInfo.InvariantCulture, $"LightGBM could not be used on this machine ({e.GetType().Name}); the model was trained with FastTree instead.");
            }
        }

        return (FitFastTree(ml, data), TreeTrainer.FastTree, note);
    }

    private BinaryPredictionTransformer<CalibratedModelParametersBase<LightGbmBinaryModelParameters, PlattCalibrator>> FitLightGbm(MLContext ml, IDataView data) =>
        ml.BinaryClassification.Trainers.LightGbm(new LightGbmBinaryTrainer.Options
        {
            LabelColumnName = "Label",
            FeatureColumnName = "Features",
            NumberOfLeaves = _options.Leaves,
            NumberOfIterations = _options.Trees,
            LearningRate = _options.LearningRate,
            MinimumExampleCountPerLeaf = _options.MinLeafRows,
            NumberOfThreads = 1,
            Seed = _options.Seed,
        }).Fit(data);

    private BinaryPredictionTransformer<CalibratedModelParametersBase<FastTreeBinaryModelParameters, PlattCalibrator>> FitFastTree(MLContext ml, IDataView data) =>
        ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
        {
            LabelColumnName = "Label",
            FeatureColumnName = "Features",
            NumberOfLeaves = _options.Leaves,
            NumberOfTrees = _options.Trees,
            LearningRate = _options.LearningRate,
            MinimumExampleCountPerLeaf = _options.MinLeafRows,
            NumberOfThreads = 1,
            Seed = _options.Seed,
        }).Fit(data);

    private sealed class Untrained(double rate, string reason) : IRowScorer, IFitNote
    {
        public string? Note => "meta_model could not be trained, so it scores the base rate: " + reason;

        public double Score(ValidationRow row) => rate;
    }
}
