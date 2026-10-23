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
        MlNetModel.Load([1, 2, 3, 4]).Error.Code.ShouldBe("ml.bad_artifact");
        MlNetModel.Load(artifact.Bytes[..40]).Error.Code.ShouldBe("ml.bad_artifact");
    }

    [Fact]
    public void When_lightgbm_cannot_be_used_the_model_says_it_was_trained_with_fasttree()
    {
        var model = Train(SyntheticRows.Make(500, learnable: true));

        if (model.Trainer == TreeTrainer.FastTree)
        {
            model.Note.ShouldNotBeNull();
            model.Note.ShouldContain("FastTree");
        }
        else
        {
            model.Note.ShouldBeNull();
        }
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
