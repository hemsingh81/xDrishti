using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;
using XDrishti.Domain.Learning;

namespace XDrishti.Learning.MlNet.Tests;

/// <summary>The calibration of a model is chosen by the number of calibration rows (CF-10 part 10a, ADR 0214), is stored in the artifact header and survives a save and a load; and the score the map receives is the trees' margin.</summary>
public sealed class MlNetCalibrationChoiceTests
{
    private static MlNetModel Train(IReadOnlyList<ValidationRow> rows, MlNetTrainerOptions? options = null) => new MlNetScorerFactory(options).Train(rows).Value;

    [Fact]
    public void Fewer_calibration_rows_than_the_cut_get_platt_scaling()
    {
        var rows = SyntheticRows.Make(600, learnable: true);

        var model = Train(rows);

        model.CalibrationRows.ShouldBe(150);
        model.Calibration.Kind.ShouldBe(CalibrationKind.Platt);
        model.Calibration.Platt!.Slope.ShouldBeGreaterThan(0);
        model.Header.CalibrationKind.ShouldBe("platt");
        model.Header.PlattSlope.ShouldBe(model.Calibration.Platt.Slope);
        model.Calibration.Scores.Count.ShouldBe(11);
    }

    [Fact]
    public void The_cut_is_the_setting_and_at_the_cut_the_isotonic_map_is_used()
    {
        var rows = SyntheticRows.Make(600, learnable: true);

        var model = Train(rows, new MlNetTrainerOptions(IsotonicMinRows: 150));

        model.Calibration.Kind.ShouldBe(CalibrationKind.Isotonic);
        model.Header.CalibrationKind.ShouldBe("isotonic");
        model.Header.PlattSlope.ShouldBe(0);
        Train(rows, new MlNetTrainerOptions(IsotonicMinRows: 151)).Calibration.Kind.ShouldBe(CalibrationKind.Platt);
    }

    [Fact]
    public void A_platt_model_scores_the_same_after_a_save_and_a_load_and_the_same_rows_give_the_same_bytes()
    {
        var rows = SyntheticRows.Make(600, learnable: true);
        var model = Train(rows);

        var artifact = model.ToArtifact();
        var loaded = MlNetModel.Load(artifact.Bytes, artifact.Sha256).Value;

        loaded.Calibration.Kind.ShouldBe(CalibrationKind.Platt);
        rows.Take(40).Select(loaded.Score).ShouldBe(rows.Take(40).Select(model.Score));
        Train(rows).ToArtifact().Sha256.ShouldBe(artifact.Sha256);
    }

    [Fact]
    public void An_isotonic_model_scores_the_same_after_a_save_and_a_load()
    {
        var rows = SyntheticRows.Make(600, learnable: true);
        var model = Train(rows, new MlNetTrainerOptions(IsotonicMinRows: 100));

        var artifact = model.ToArtifact();
        var loaded = MlNetModel.Load(artifact.Bytes, artifact.Sha256).Value;

        loaded.Calibration.Kind.ShouldBe(CalibrationKind.Isotonic);
        rows.Take(40).Select(loaded.Score).ShouldBe(rows.Take(40).Select(model.Score));
    }

    [Fact]
    public void A_header_stored_before_the_change_reads_as_isotonic_and_a_bad_kind_or_slope_is_refused()
    {
        var model = Train(SyntheticRows.Make(600, learnable: true), new MlNetTrainerOptions(IsotonicMinRows: 100));
        var (header, zip) = ArtifactCodec.Unpack(model.ToArtifact().Bytes)!.Value;
        var json = JsonSerializer.SerializeToNode(header)!.AsObject();
        foreach (var key in new[] { "CalibrationKind", "PlattSlope", "PlattCenter", "PlattIntercept" })
        {
            json.Remove(key);
        }

        var old = JsonSerializer.Deserialize<ArtifactHeader>(json.ToJsonString())!;

        old.CalibrationKind.ShouldBe("isotonic");
        MlNetModel.LoadUnverified(ArtifactCodec.Pack(old, zip).Bytes).Value.Calibration.Kind.ShouldBe(CalibrationKind.Isotonic);
        MlNetModel.LoadUnverified(ArtifactCodec.Pack(header with { CalibrationKind = "sigmoid" }, zip).Bytes).Error.Code.ShouldBe("ml.bad_artifact");
        MlNetModel.LoadUnverified(ArtifactCodec.Pack(header with { PlattSlope = -1 }, zip).Bytes).Error.Code.ShouldBe("ml.bad_artifact");
    }

    [Fact]
    public void An_isotonic_artifact_keeps_format_version_1_and_a_platt_artifact_is_version_2()
    {
        var rows = SyntheticRows.Make(600, learnable: true);
        var iso = Train(rows, new MlNetTrainerOptions(IsotonicMinRows: 100));
        var platt = Train(rows);

        iso.Header.Version.ShouldBe(1);
        platt.Header.Version.ShouldBe(2);
        var isoArtifact = iso.ToArtifact();
        MlNetModel.Load(isoArtifact.Bytes, isoArtifact.Sha256).Value.Header.Version.ShouldBe(1);
        Train(rows, new MlNetTrainerOptions(IsotonicMinRows: 100)).ToArtifact().Sha256.ShouldBe(isoArtifact.Sha256, "the isotonic bytes and hash are as stable as before");
        var plattArtifact = platt.ToArtifact();
        var loaded = MlNetModel.Load(plattArtifact.Bytes, plattArtifact.Sha256).Value;
        loaded.Header.Version.ShouldBe(2);
        loaded.Calibration.Kind.ShouldBe(CalibrationKind.Platt);
    }

    [Fact]
    public void The_version_and_the_calibration_kind_must_agree_or_the_artifact_is_refused()
    {
        var rows = SyntheticRows.Make(600, learnable: true);
        var (isoHeader, isoZip) = ArtifactCodec.Unpack(Train(rows, new MlNetTrainerOptions(IsotonicMinRows: 100)).ToArtifact().Bytes)!.Value;
        var (plattHeader, plattZip) = ArtifactCodec.Unpack(Train(rows).ToArtifact().Bytes)!.Value;

        MlNetModel.LoadUnverified(ArtifactCodec.Pack(isoHeader with { Version = 2 }, isoZip).Bytes).Error.Code.ShouldBe("ml.bad_artifact");
        MlNetModel.LoadUnverified(ArtifactCodec.Pack(plattHeader with { Version = 1 }, plattZip).Bytes).Error.Code.ShouldBe("ml.bad_artifact");
        MlNetModel.LoadUnverified(ArtifactCodec.Pack(plattHeader, plattZip).Bytes).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_cut_must_be_in_range()
    {
        new MlNetTrainerOptions(IsotonicMinRows: 0).Problems().ShouldNotBeEmpty();
        new MlNetTrainerOptions().Problems().ShouldBeEmpty();
        new MlNetScorerFactory(new MlNetTrainerOptions(IsotonicMinRows: 0)).Train(SyntheticRows.Make(600, learnable: true)).Error.Code.ShouldBe("ml.bad_options");
    }

    [Fact]
    public void The_map_receives_the_trees_margin_and_not_the_Platt_probability_that_ML_NET_adds_beside_it()
    {
        // The ML.NET binary trainers return a calibrated transformer with two columns: Score (the margin) and Probability (ML.NET's own Platt map of it). MlNetModel.RawScore reads Score.
        var rows = SyntheticRows.Make(600, learnable: true);
        var ml = new MLContext(seed: 1);
        var examples = rows.Select(r => new Example { Features = MlNetModel.Impute(r.Features, [0, 0, 0]), Label = r.Label == 1 });
        var data = Example.View(ml, examples, 3);
        var trees = ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options { LabelColumnName = "Label", FeatureColumnName = "Features", NumberOfThreads = 1, Seed = 1 }).Fit(data);
        var transformed = trees.Transform(data);

        var score = transformed.GetColumn<float>("Score").ToArray();
        var probability = transformed.GetColumn<float>("Probability").ToArray();

        score.Any(s => s is < 0 or > 1).ShouldBeTrue("the margin runs outside 0 to 1");
        probability.All(p => p is >= 0 and <= 1).ShouldBeTrue();
        score.Zip(probability, (s, p) => Math.Abs(s - p)).Max().ShouldBeGreaterThan(0.1f);
        var model = Train(rows);
        rows.Select(model.RawScore).Any(s => s is < 0 or > 1).ShouldBeTrue("RawScore is the margin, so the calibration map is the only map applied to it");
    }
}
