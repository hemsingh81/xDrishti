using XDrishti.Domain.Learning;

namespace XDrishti.Learning.MlNet.Tests;

/// <summary>Training, calibration, the no-leak split, determinism and the explanation of the ML.NET adapter (P6-06, ADR 0068).</summary>
public sealed class MlNetScorerFactoryTests
{
    private static readonly MlNetScorerFactory Factory = new();

    private static MlNetModel Train(IReadOnlyList<ValidationRow> rows, MlNetTrainerOptions? options = null) => new MlNetScorerFactory(options).Train(rows).Value;

    [Fact]
    public void A_learnable_signal_is_found_and_scored_better_than_the_base_rate_out_of_sample()
    {
        var rows = SyntheticRows.Make(900, learnable: true);
        var model = Train(rows[..600]);
        var test = rows[600..];

        var scores = test.Select(model.Score).ToArray();
        var labels = test.Select(r => r.Label).ToArray();
        var baseRate = BaseRateScorer.Rate(rows[..600]);

        var model_ = MetricsCalculator.Compute(scores, labels);
        model_.LogLoss.ShouldBeLessThan(MetricsCalculator.Compute(test.Select(_ => baseRate).ToArray(), labels).LogLoss - 0.03);
        model_.Auc!.Value.ShouldBeGreaterThan(0.75);
    }

    [Fact]
    public void The_planted_feature_has_the_largest_contribution_with_the_right_sign()
    {
        var rows = SyntheticRows.Make(900, learnable: true);
        var model = Train(rows[..600]);

        var high = model.Explain(SyntheticRows.WithFeatures(rows[0], 2.0, 0, 0), 3);
        var low = model.Explain(SyntheticRows.WithFeatures(rows[0], -2.0, 0, 0), 3);

        high[0].Index.ShouldBe(0);
        high[0].Value.ShouldBeGreaterThan(0);
        low[0].Index.ShouldBe(0);
        low[0].Value.ShouldBeLessThan(0);
        model.Importance(rows[600..700]).Select((v, i) => (v, i)).MaxBy(t => t.v).i.ShouldBe(0);
    }

    [Fact]
    public void Explain_returns_at_most_top_contributions_largest_first()
    {
        var rows = SyntheticRows.Make(600, learnable: true);
        var model = Train(rows);

        var explained = model.Explain(rows[10], 2);

        explained.Count.ShouldBeLessThanOrEqualTo(2);
        explained.Select(c => Math.Abs(c.Value)).ShouldBe(explained.Select(c => Math.Abs(c.Value)).OrderDescending());
    }

    [Fact]
    public void The_calibrated_probability_never_falls_as_the_raw_score_rises_and_stays_inside_the_floor()
    {
        var rows = SyntheticRows.Make(700, learnable: true);
        var model = Train(rows);

        var pairs = rows.Select(r => (Raw: model.RawScore(r), P: model.Score(r))).OrderBy(t => t.Raw).ToArray();

        for (var i = 1; i < pairs.Length; i++)
        {
            pairs[i].P.ShouldBeGreaterThanOrEqualTo(pairs[i - 1].P);
        }

        pairs.ShouldAllBe(t => t.P >= 0.005 && t.P <= 0.995);
        model.Calibration.Values.Order().ShouldBe(model.Calibration.Values);
    }

    [Fact]
    public void The_held_back_calibration_rows_are_the_latest_and_never_help_the_trees()
    {
        var rows = SyntheticRows.Make(600, learnable: true);
        var model = Train(rows);
        var heldStart = rows.Count - model.CalibrationRows;

        var flipped = rows.Select((r, i) => i >= heldStart ? SyntheticRows.WithLabel(r, 1 - r.Label) : r).ToList();
        var other = Train(flipped);

        model.CalibrationRows.ShouldBe((int)Math.Ceiling(600 * 0.25));
        rows.Take(10).Select(model.RawScore).ShouldBe(rows.Take(10).Select(other.RawScore), "labels of the held-back rows do not change the trees");
        rows.Take(10).Select(model.Score).ShouldNotBe(rows.Take(10).Select(other.Score), "but they do change the calibration map");
    }

    [Fact]
    public void Labels_of_the_fit_rows_do_change_the_trees()
    {
        var rows = SyntheticRows.Make(600, learnable: true);
        var model = Train(rows);

        var flipped = rows.Select((r, i) => i < 100 ? SyntheticRows.WithLabel(r, 1 - r.Label) : r).ToList();
        var other = Train(flipped);

        rows.Take(20).Select(model.RawScore).ShouldNotBe(rows.Take(20).Select(other.RawScore));
    }

    [Fact]
    public void A_fit_row_whose_horizon_reaches_the_held_back_rows_is_dropped()
    {
        var rows = SyntheticRows.Make(600, learnable: true);
        var heldStart = 600 - (int)Math.Ceiling(600 * 0.25);
        var clean = Train(rows);
        var long_ = rows.Select((r, i) => i is 440 or 441 ? r with { ResolvedEpochSeconds = rows[heldStart].DecisionEpochSeconds + 10 } : r).ToList();

        var model = Train(long_);

        model.FitRows.ShouldBe(clean.FitRows - 2);
        model.CalibrationRows.ShouldBe(clean.CalibrationRows);
    }

    [Fact]
    public void The_same_rows_in_any_order_give_the_same_artifact_bytes_and_hash()
    {
        var rows = SyntheticRows.Make(500, learnable: true);

        var first = Train(rows).ToArtifact();
        var second = Train([.. rows.AsEnumerable().Reverse()]).ToArtifact();

        second.Sha256.ShouldBe(first.Sha256);
        second.Bytes.ShouldBe(first.Bytes);
        first.Sha256.Length.ShouldBe(64);
    }

    [Fact]
    public void A_different_seed_gives_a_different_model()
    {
        var rows = SyntheticRows.Make(500, learnable: true);

        Train(rows).ToArtifact().Sha256.ShouldNotBe(Train(rows, new MlNetTrainerOptions(Seed: 99)).ToArtifact().Sha256);
    }

    [Fact]
    public void A_saved_model_loads_scores_and_saves_the_same()
    {
        var rows = SyntheticRows.Make(500, learnable: true);
        var model = Train(rows);
        var artifact = model.ToArtifact();

        var loaded = MlNetModel.Load(artifact.Bytes, artifact.Sha256).Value;

        rows.Select(loaded.Score).ShouldBe(rows.Select(model.Score));
        rows.Take(20).Select(r => loaded.Explain(r, 3)).ShouldBe(rows.Take(20).Select(r => model.Explain(r, 3)));
        loaded.ToArtifact().Sha256.ShouldBe(artifact.Sha256);
        loaded.Trainer.ShouldBe(model.Trainer);
        loaded.Note.ShouldBe(model.Note);
    }

    [Fact]
    public void A_wrong_hash_or_foreign_bytes_are_a_failure_not_an_exception()
    {
        var artifact = Train(SyntheticRows.Make(500, learnable: true)).ToArtifact();

        MlNetModel.Load(artifact.Bytes, new string('0', 64)).Error.Code.ShouldBe("ml.hash_mismatch");
        MlNetModel.LoadUnverified([1, 2, 3, 4]).Error.Code.ShouldBe("ml.bad_artifact");
        MlNetModel.LoadUnverified(artifact.Bytes[..40]).Error.Code.ShouldBe("ml.bad_artifact");
        MlNetModel.LoadUnverified(artifact.Bytes[..(artifact.Bytes.Length / 2)]).Error.Code.ShouldBe("ml.bad_artifact");
        Should.Throw<ArgumentException>(() => MlNetModel.Load(artifact.Bytes, " "));
    }

    [Fact]
    public void Contributions_are_path_attributions_that_follow_the_margin_but_are_not_an_exact_decomposition()
    {
        var rows = SyntheticRows.Make(700, learnable: true);
        var model = Train(rows, new MlNetTrainerOptions(Preferred: TreeTrainer.FastTree));
        var sample = rows.Take(200).ToArray();

        var sums = sample.Select(r => model.Explain(r, model.Width).Sum(c => c.Value)).ToArray();
        var raw = sample.Select(model.RawScore).ToArray();

        // Documented in the model's remarks: they are not asserted to equal the margin (no bias term, path-based), only to track it.
        Correlation(sums, raw).ShouldBeGreaterThan(0.9);
    }

    [Fact]
    public void A_missing_value_is_imputed_to_the_mean_and_explains_as_that_mean()
    {
        var rows = SyntheticRows.Make(600, learnable: true);
        var model = Train(rows);
        var mean = model.Header.Means[0];

        var missing = SyntheticRows.WithFeatures(rows[0], double.NaN, 0, 0);
        var atMean = SyntheticRows.WithFeatures(rows[0], mean, 0, 0);

        model.Explain(missing, 3).ShouldBe(model.Explain(atMean, 3));
        model.RawScore(missing).ShouldBe(model.RawScore(atMean), 1e-5);
    }

    [Fact]
    public void When_lightgbm_cannot_be_loaded_the_model_is_fasttree_and_the_note_says_why()
    {
        var factory = new MlNetScorerFactory(null, (_, _) => throw new DllNotFoundException("lib_lightgbm.so not found"));

        var model = factory.Train(SyntheticRows.Make(500, learnable: true)).Value;

        model.Trainer.ShouldBe(TreeTrainer.FastTree);
        model.Note.ShouldNotBeNull();
        model.Note.ShouldContain("FastTree");
        model.Note.ShouldContain("DllNotFoundException");
        model.Note.ShouldContain("lib_lightgbm.so not found");
        MlNetModel.Load(model.ToArtifact().Bytes, model.ToArtifact().Sha256).Value.Note.ShouldBe(model.Note);
    }

    [Fact]
    public void A_lightgbm_failure_that_is_not_a_load_failure_is_not_hidden()
    {
        var factory = new MlNetScorerFactory(null, (_, _) => throw new InvalidOperationException("bad data"));

        Should.Throw<InvalidOperationException>(() => factory.Train(SyntheticRows.Make(500, learnable: true)));
    }

    [Fact]
    public void Lightgbm_is_used_and_round_trips_where_its_native_library_loads()
    {
        var model = Train(SyntheticRows.Make(500, learnable: true));
        Assert.SkipWhen(model.Trainer == TreeTrainer.FastTree, "The LightGBM native library does not load on this machine (" + model.Note + "); the fall-back is covered by its own test.");

        model.Note.ShouldBeNull();
        var artifact = model.ToArtifact();
        var loaded = MlNetModel.Load(artifact.Bytes, artifact.Sha256).Value;
        loaded.Trainer.ShouldBe(TreeTrainer.LightGbm);
        loaded.Explain(SyntheticRows.Make(5, learnable: true)[0], 3).ShouldNotBeEmpty();
    }

    [Fact]
    public void Tampered_bytes_fail_the_hash_and_a_tampered_header_or_zip_directory_is_a_failure_not_an_exception()
    {
        var artifact = Train(SyntheticRows.Make(500, learnable: true)).ToArtifact();
        var headerEnd = 12 + BitConverter.ToInt32(artifact.Bytes, 8);
        // The tree payload is not flipped on purpose: ML.NET's deserialiser is not hardened against crafted trees (a cyclic tree can spin), so only the hash protects it; see ADR 0068.
        var positions = Enumerable.Range(0, headerEnd).Where(i => i % 3 == 0).Concat(Enumerable.Range(artifact.Bytes.Length - 200, 200)).ToArray();

        foreach (var at in positions)
        {
            var bytes = (byte[])artifact.Bytes.Clone();
            bytes[at] ^= 0xFF;
            MlNetModel.Load(bytes, artifact.Sha256).Error.Code.ShouldBe("ml.hash_mismatch");
            var result = MlNetModel.LoadUnverified(bytes);
            if (result.IsFailure)
            {
                result.Error.Code.ShouldBe("ml.bad_artifact");
            }
        }
    }

    [Fact]
    public void A_header_with_missing_or_out_of_range_fields_is_refused_at_load()
    {
        var artifact = Train(SyntheticRows.Make(500, learnable: true)).ToArtifact();
        var (header, zip) = ArtifactCodec.Unpack(artifact.Bytes)!.Value;

        Refuse(header with { Means = null! }, zip);
        Refuse(header with { CalibrationScores = null! }, zip);
        Refuse(header with { CalibrationValues = null! }, zip);
        Refuse(header with { CalibrationValues = [] }, zip);
        Refuse(header with { ProbabilityFloor = 0 }, zip);
        Refuse(header with { ProbabilityFloor = 0.5 }, zip);
        Refuse(header with { ProbabilityFloor = -0.1 }, zip);
        Refuse(header with { Version = 2 }, zip);
        Refuse(header with { Trainer = (TreeTrainer)9 }, zip);
        Refuse(header with { Trainer = header.Trainer == TreeTrainer.FastTree ? TreeTrainer.LightGbm : TreeTrainer.FastTree }, zip);
        Refuse(header with { Width = 2, Means = [0, 0] }, zip);
        Refuse(header with { Width = header.Width + 1, Means = [.. header.Means, 0.0] }, zip);
        Refuse(header with { CalibrationScores = [.. header.CalibrationScores.Reverse()] }, zip);
        ArtifactCodec.ZipIsSane([1, 2, 3]).ShouldBeFalse();

        MlNetModel.LoadUnverified(ArtifactCodec.Pack(header, zip).Bytes).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_oversize_artifact_is_refused_before_it_is_hashed_or_read()
    {
        var big = new byte[ArtifactCodec.MaxArtifactBytes + 1];

        MlNetModel.Load(big, new string('0', 64)).Error.Code.ShouldBe("ml.bad_artifact");
        MlNetModel.LoadUnverified(big).Error.Code.ShouldBe("ml.bad_artifact");
    }

    [Fact]
    public void A_header_longer_than_the_cap_is_refused()
    {
        var bytes = new byte[40];
        "XDMODEL1"u8.CopyTo(bytes);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), ArtifactCodec.MaxHeaderBytes + 1);

        ArtifactCodec.Unpack(bytes).ShouldBeNull();
    }

    private static void Refuse(ArtifactHeader header, byte[] zip) =>
        MlNetModel.LoadUnverified(ArtifactCodec.Pack(header, zip).Bytes).Error.Code.ShouldBe("ml.bad_artifact");

    private static double Correlation(double[] x, double[] y)
    {
        var mx = x.Average();
        var my = y.Average();
        var cov = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
        return cov / Math.Sqrt(x.Sum(a => (a - mx) * (a - mx)) * y.Sum(b => (b - my) * (b - my)));
    }

    [Fact]
    public void Fasttree_can_be_asked_for_and_then_has_no_note()
    {
        var model = Train(SyntheticRows.Make(500, learnable: true), new MlNetTrainerOptions(Preferred: TreeTrainer.FastTree));

        model.Trainer.ShouldBe(TreeTrainer.FastTree);
        model.Note.ShouldBeNull();
    }

    [Fact]
    public void Rows_that_cannot_be_trained_on_fail_with_a_code_and_fit_falls_back_to_the_base_rate_with_a_note()
    {
        var few = SyntheticRows.Make(30, learnable: true);
        var oneClass = SyntheticRows.Make(500, learnable: true).Select(r => SyntheticRows.WithLabel(r, 1)).ToList();
        var noFeatures = SyntheticRows.Make(500, learnable: true).Select(r => r with { Features = [] }).ToList();

        Factory.Train(few).Error.Code.ShouldBe("ml.too_few_rows");
        Factory.Train(oneClass).Error.Code.ShouldBe("ml.one_class");
        Factory.Train(noFeatures).Error.Code.ShouldBe("ml.no_features");
        Factory.Train([]).Error.Code.ShouldBe("ml.no_features");
        new MlNetScorerFactory(new MlNetTrainerOptions(Trees: 0)).Train(few).Error.Code.ShouldBe("ml.bad_options");
        var fallback = Factory.Fit(few);
        fallback.Score(few[0]).ShouldBe(BaseRateScorer.Rate(few));
        ((IFitNote)fallback).Note.ShouldNotBeNull();
    }

    [Fact]
    public void Missing_and_infinite_feature_values_read_as_the_mean_and_a_wrong_width_is_refused()
    {
        var rows = SyntheticRows.Make(500, learnable: true);
        var model = Train(rows);

        var broken = SyntheticRows.WithFeatures(rows[0], double.NaN, double.PositiveInfinity, double.NaN);

        double.IsFinite(model.Score(broken)).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => model.Score(SyntheticRows.WithFeatures(rows[0], 1, 2)));
    }

    [Fact]
    public void The_model_is_a_scorer_factory_the_harness_accepts_and_names()
    {
        IReadOnlyList<IScorerFactory> candidates = [Factory];

        candidates[0].Name.ShouldBe("meta_model");
        candidates[0].Fit(SyntheticRows.Make(300, learnable: true)).ShouldBeAssignableTo<IExplainableScorer>();
    }
}
