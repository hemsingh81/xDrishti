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
/// A trained model (P6-06, ADR 0068): imputation means, a tree ensemble, a PAV calibration map. <see cref="Score"/> is the calibrated probability of label 1; <see cref="RawScore"/> is the margin of the trees; <see cref="Explain"/> gives path-based
/// per-feature attributions in raw-score units: they point the same way as the margin and rank the features, but they are not exact (they do not add up to <see cref="RawScore"/>), not SHAP, and the bias term is not included. A missing or
/// non-finite feature value is imputed to the fit mean and then appears as an ordinary contribution of that mean value. Thread-safe: one scoring call at a time per model.
/// </summary>
public sealed class MlNetModel : IExplainableScorer, IFitNote
{
    private readonly MLContext _ml;
    private readonly ITransformer _trees;
    private readonly double[] _means;
    private readonly Isotonic _calibration;
    private readonly double _floor;
    private readonly Lock _gate = new();
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

    /// <summary>
    /// Restores a model from its artifact after checking the bytes against the stored SHA-256. The hash is an integrity check (the bytes are the ones that were stored), not authenticity: whoever can replace the file can replace the hash.
    /// </summary>
    /// <param name="bytes">The artifact.</param>
    /// <param name="expectedSha256">The stored hash (hex).</param>
    public static Result<MlNetModel> Load(byte[] bytes, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        if (bytes.Length > ArtifactCodec.MaxArtifactBytes)
        {
            return TooBig();
        }

        if (!string.Equals(ArtifactCodec.Hash(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<MlNetModel>(Error.Validation("ml.hash_mismatch", "The model bytes do not match their stored SHA-256."));
        }

        return LoadUnverified(bytes);
    }

    /// <summary>Restores a model without a hash check: for tests and tools that have no stored hash. Everything else about the bytes is still validated.</summary>
    /// <param name="bytes">The artifact.</param>
    public static Result<MlNetModel> LoadUnverified(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > ArtifactCodec.MaxArtifactBytes)
        {
            return TooBig();
        }

        try
        {
            if (ArtifactCodec.Unpack(bytes) is not var (header, zip) || !header.IsUsable() || !ArtifactCodec.ZipIsSane(zip))
            {
                return Bad("The bytes are not a model artifact this code can read.");
            }

            var ml = new MLContext(seed: header.Seed);
            using var stream = new MemoryStream(zip);
            var trees = ml.Model.Load(stream, out _);
            var calibration = Isotonic.FromKnots(header.CalibrationScores, header.CalibrationValues);
            var model = new MlNetModel(ml, trees, header, calibration);
            return model.Check() is { } problem ? Bad(problem) : Result.Success(model);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or ThreadAbortException or AccessViolationException))
        {
            // ML.NET's loader throws whatever the bytes make it throw (KeyNotFound, Overflow, InvalidCast, NotSupported, FileLoad, TargetInvocation, ...); the bytes are untrusted, so every ordinary failure is a failed load.
            return Bad("The model could not be loaded: " + e.Message);
        }
    }

    private static Result<MlNetModel> TooBig() => Bad("The artifact is larger than " + ArtifactCodec.MaxArtifactBytes + " bytes.");

    private static Result<MlNetModel> Bad(string message) => Result.Failure<MlNetModel>(Error.Validation("ml.bad_artifact", message));

    /// <summary>Settles at load what would otherwise fail at the first call: the transformer type, its input width, a first score and the contribution calculator.</summary>
    private string? Check()
    {
        var inputType = (Header.Trainer, _trees) switch
        {
            (TreeTrainer.LightGbm, BinaryPredictionTransformer<CalibratedModelParametersBase<LightGbmBinaryModelParameters, PlattCalibrator>> t) => t.FeatureColumnType,
            (TreeTrainer.FastTree, BinaryPredictionTransformer<CalibratedModelParametersBase<FastTreeBinaryModelParameters, PlattCalibrator>> t) => t.FeatureColumnType,
            _ => null,
        };
        if (inputType is null)
        {
            return "The model is not the calibrated binary tree model that the header names.";
        }

        if (inputType is not VectorDataViewType { Size: var size } || size != Width)
        {
            return "The model reads a different number of features than the header says.";
        }

        // A few probes (zeros, means, far below, far above) so that a corrupt tree fails here and not on the first real row. Bytes whose hash was not checked can still hold a tree that fails only on some other row.
        float[][] probes = [new float[Width], [.. _means.Select(m => (float)m)], [.. Enumerable.Repeat(-1e6f, Width)], [.. Enumerable.Repeat(1e6f, Width)]];
        _contributor ??= BuildContributor();
        return probes.All(p => double.IsFinite(Margin(new Example { Features = p }))) ? null : "The model gives a non-finite score.";
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
        return Margin(ToExample(row));
    }

    private double Margin(Example example)
    {
        lock (_gate)
        {
            var data = Example.View(_ml, [example], Width);
            return _trees.Transform(data).GetColumn<float>("Score").First();
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
