using System.Collections.Immutable;
using Microsoft.ML;
using Microsoft.ML.Calibrators;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;
using Microsoft.ML.Trainers.LightGbm;
using Microsoft.ML.Transforms;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using Isotonic = XDrishti.Domain.Learning.IsotonicCalibrator;

namespace XDrishti.Learning.MlNet;

/// <summary>
/// A trained model (P6-06, ADR 0068): imputation means, a tree ensemble, a PAV calibration map. <see cref="Score"/> is the calibrated probability of label 1; <see cref="RawScore"/> is the margin of the trees; <see cref="Explain"/> gives the per-feature
/// contributions to the margin (tree-path contributions, in raw-score units: they add up to the margin, not to the calibrated probability). Thread-safe: one scoring call at a time per model.
/// </summary>
public sealed class MlNetModel : IExplainableScorer, IFitNote
{
    private readonly MLContext _ml;
    private readonly ITransformer _trees;
    private readonly double[] _means;
    private readonly Isotonic _calibration;
    private readonly double _floor;
    private readonly Lock _gate = new();
    private PredictionEngine<Example, RawOutput>? _engine;
    private FeatureContributionCalculatingTransformer? _contributor;

    internal MlNetModel(MLContext ml, ITransformer trees, ArtifactHeader header, Isotonic calibration)
    {
        _ml = ml;
        _trees = trees;
        Header = header;
        _means = header.Means;
        _calibration = calibration;
        _floor = header.ProbabilityFloor;
    }

    /// <summary>The trainer that was used (FastTree when LightGBM could not be loaded or trained).</summary>
    public TreeTrainer Trainer => Header.Trainer;

    /// <summary>The number of features the model reads.</summary>
    public int Width => Header.Width;

    /// <summary>Rows the trees were fitted on.</summary>
    public int FitRows => Header.FitRows;

    /// <summary>Rows the calibration was fitted on (never rows the trees saw).</summary>
    public int CalibrationRows => Header.CalibrationRows;

    /// <inheritdoc />
    public string? Note => Header.Note;

    /// <summary>The calibration map.</summary>
    public Isotonic Calibration => _calibration;

    internal ArtifactHeader Header { get; }

    /// <summary>Restores a model from its artifact; the optional hash is checked first.</summary>
    /// <param name="bytes">The artifact.</param>
    /// <param name="expectedSha256">The stored hash, or null to skip the check.</param>
    public static Result<MlNetModel> Load(byte[] bytes, string? expectedSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (expectedSha256 is not null && !string.Equals(ArtifactCodec.Hash(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<MlNetModel>(Error.Validation("ml.hash_mismatch", "The model bytes do not match their stored SHA-256."));
        }

        if (ArtifactCodec.Unpack(bytes) is not var (header, zip) || header.Version != 1 || header.Width < 1 || header.Means.Length != header.Width)
        {
            return Result.Failure<MlNetModel>(Error.Validation("ml.bad_artifact", "The bytes are not a model artifact this code can read."));
        }

        try
        {
            var ml = new MLContext(seed: header.Seed);
            using var stream = new MemoryStream(zip);
            var trees = ml.Model.Load(stream, out _);
            var calibration = Isotonic.FromKnots(header.CalibrationScores, header.CalibrationValues);
            return Result.Success(new MlNetModel(ml, trees, header, calibration));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or InvalidDataException or FormatException)
        {
            return Result.Failure<MlNetModel>(Error.Validation("ml.bad_artifact", "The model could not be loaded: " + e.Message));
        }
    }

    /// <summary>The bytes of the model and their SHA-256. The same trained model gives the same bytes.</summary>
    public ModelArtifact ToArtifact()
    {
        using var stream = new MemoryStream();
        _ml.Model.Save(_trees, Example.View(_ml, [new Example { Features = new float[Width] }], Width).Schema, stream);
        return ArtifactCodec.Pack(Header, stream.ToArray());
    }

    /// <summary>The margin of the trees before calibration.</summary>
    public double RawScore(ValidationRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var example = ToExample(row);
        lock (_gate)
        {
            _engine ??= _ml.Model.CreatePredictionEngine<Example, RawOutput>(_trees, inputSchemaDefinition: Schema());
            return _engine.Predict(example).Score;
        }
    }

    /// <inheritdoc />
    public double Score(ValidationRow row)
    {
        var p = _calibration.Apply(RawScore(row));
        return Math.Clamp(p, _floor, 1 - _floor);
    }

    /// <inheritdoc />
    public IReadOnlyList<FeatureContribution> Explain(ValidationRow row, int top)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegative(top);
        var example = ToExample(row);
        float[] values;
        lock (_gate)
        {
            _contributor ??= BuildContributor();
            var data = Example.View(_ml, [example], Width);
            values = _contributor.Transform(data).GetColumn<float[]>("FeatureContributions").First();
        }

        return [.. Enumerable.Range(0, Math.Min(values.Length, Width))
            .Select(i => new FeatureContribution(i, values[i]))
            .Where(c => c.Value != 0)
            .OrderByDescending(c => Math.Abs(c.Value))
            .ThenBy(c => c.Index)
            .Take(top)];
    }

    /// <summary>The global importance of each feature: the mean absolute contribution over the rows. Index i is feature i.</summary>
    public IReadOnlyList<double> Importance(IReadOnlyList<ValidationRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var sums = new double[Width];
        foreach (var row in rows)
        {
            foreach (var c in Explain(row, Width))
            {
                sums[c.Index] += Math.Abs(c.Value);
            }
        }

        return [.. sums.Select(s => rows.Count == 0 ? 0 : s / rows.Count)];
    }

    internal Example ToExample(ValidationRow row)
    {
        if (row.Features.Length != Width)
        {
            throw new ArgumentException($"The row has {row.Features.Length} features, the model reads {Width}.", nameof(row));
        }

        return new Example { Features = Impute(row.Features, _means) };
    }

    internal static float[] Impute(ImmutableArray<double> features, double[] means)
    {
        var x = new float[means.Length];
        for (var j = 0; j < x.Length; j++)
        {
            x[j] = (float)(double.IsFinite(features[j]) ? features[j] : means[j]);
        }

        return x;
    }

    private SchemaDefinition Schema()
    {
        var schema = SchemaDefinition.Create(typeof(Example));
        schema[nameof(Example.Features)].ColumnType = new VectorDataViewType(NumberDataViewType.Single, Width);
        return schema;
    }

    private FeatureContributionCalculatingTransformer BuildContributor()
    {
        var sample = Example.View(_ml, [new Example { Features = new float[Width] }], Width);
        return _trees switch
        {
            BinaryPredictionTransformer<CalibratedModelParametersBase<LightGbmBinaryModelParameters, PlattCalibrator>> lightGbm =>
                _ml.Transforms.CalculateFeatureContribution(lightGbm, Width, Width, normalize: false).Fit(sample),
            BinaryPredictionTransformer<CalibratedModelParametersBase<FastTreeBinaryModelParameters, PlattCalibrator>> fastTree =>
                _ml.Transforms.CalculateFeatureContribution(fastTree, Width, Width, normalize: false).Fit(sample),
            _ => throw new NotSupportedException("The loaded model is not a calibrated LightGBM or FastTree binary model."),
        };
    }
}
