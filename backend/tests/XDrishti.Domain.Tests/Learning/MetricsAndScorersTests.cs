using System.Collections.Immutable;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The metrics and the three baselines against hand-computed tables (P6-05, ADR 0067).</summary>
public sealed class MetricsAndScorersTests
{
    private static ValidationRow Row(string key, int label, string cell = "A", params double[] features) =>
        new(key, label, label == 1 ? 1 : -1, new DateOnly(2025, 1, 1), 0, 0, cell, [.. features]);

    [Fact]
    public void Metrics_match_the_hand_table()
    {
        // log loss = -(ln .9 + ln .8 + ln .7 + ln .8) / 4; brier = (.01 + .04 + .09 + .04) / 4
        var m = MetricsCalculator.Compute([0.9, 0.8, 0.3, 0.2], [1, 1, 0, 0], bins: 2);

        m.LogLoss.ShouldBe(0.227081, 1e-5);
        m.Brier.ShouldBe(0.045, 1e-12);
        m.Auc.ShouldBe(1.0);
        m.EcePp.ShouldBe(20.0, 1e-9); // bins {.2,.3} mean .25 vs 0 and {.8,.9} mean .85 vs 1: .5*.25 + .5*.15
        m.Lift.ShouldBe(2.0); // top fifth is one row, a winner: 1 / (2 / 4)
    }

    [Fact]
    public void Tied_scores_share_their_rank_and_a_single_class_has_no_auc()
    {
        MetricsCalculator.Compute([0.5, 0.5, 0.5, 0.5], [1, 0, 1, 0]).Auc.ShouldBe(0.5);
        MetricsCalculator.Compute([0.1, 0.9], [1, 1]).Auc.ShouldBeNull();
        MetricsCalculator.Compute([0.1, 0.9], [0, 0]).Lift.ShouldBeNull();
    }

    [Fact]
    public void A_certain_wrong_probability_has_a_finite_log_loss()
    {
        double.IsFinite(MetricsCalculator.Compute([0.0], [1]).LogLoss).ShouldBeTrue();
    }

    [Fact]
    public void Base_rate_and_cell_rate_match_the_hand_table()
    {
        // A: 1,1,0   B: 0,0  -> 2 wins in 5; base = 3/7; A = (2 + 20 * 3/7) / 23
        ValidationRow[] train = [Row("1", 1), Row("2", 1), Row("3", 0), Row("4", 0, "B"), Row("5", 0, "B")];

        new BaseRateScorer().Fit(train).Score(Row("x", 0)).ShouldBe(3.0 / 7.0, 1e-12);
        var cell = new CellRateScorer().Fit(train);
        cell.Score(Row("x", 0, "A")).ShouldBe((2 + (20 * 3.0 / 7.0)) / 23.0, 1e-12);
        cell.Score(Row("y", 0, "unseen")).ShouldBe(3.0 / 7.0, 1e-12);
    }

    [Fact]
    public void Logistic_learns_a_separating_feature_and_is_deterministic()
    {
        var train = Enumerable.Range(0, 60).Select(i => Row("k" + i, i % 2, "A", ((i % 2) * 2.0) + ((i % 5) * 0.1))).ToArray();

        var a = new LogisticScorer().Fit(train);
        var b = new LogisticScorer().Fit(train);

        a.Score(Row("hi", 1, "A", 2.0)).ShouldBeGreaterThan(0.8);
        a.Score(Row("lo", 0, "A", 0.0)).ShouldBeLessThan(0.2);
        a.Score(Row("hi", 1, "A", 2.0)).ShouldBe(b.Score(Row("hi", 1, "A", 2.0)));
    }

    [Fact]
    public void Logistic_without_features_or_with_one_class_is_the_base_rate_and_a_missing_value_counts_as_the_mean()
    {
        ValidationRow[] none = [Row("1", 1), Row("2", 0), Row("3", 0)];
        ValidationRow[] one = [Row("1", 1, "A", 1), Row("2", 1, "A", 2)];

        new LogisticScorer().Fit(none).Score(Row("x", 0)).ShouldBe(2.0 / 5.0, 1e-12);
        new LogisticScorer().Fit(one).Score(Row("x", 0, "A", 5)).ShouldBe(3.0 / 4.0, 1e-12);

        var train = Enumerable.Range(0, 40).Select(i => Row("k" + i, i % 2, "A", i % 2)).ToArray();
        var scorer = new LogisticScorer().Fit(train);
        double.IsFinite(scorer.Score(Row("n", 0, "A", double.NaN))).ShouldBeTrue();
    }

    [Fact]
    public void Row_features_default_to_empty()
    {
        var row = new ValidationRow("k", 1, 1, new DateOnly(2025, 1, 1), 0, 0, "A", default(ImmutableArray<double>));

        row.Features.IsDefault.ShouldBeTrue();
        new LogisticScorer().Fit([Row("1", 1), Row("2", 0)]).Score(row).ShouldBe(0.5, 1e-12);
    }
}
