namespace XDrishti.MlSpike.Tests;

public class GeneratorTests
{
    [Fact]
    public void The_same_seed_gives_identical_rows_and_a_different_seed_does_not()
    {
        var a = Generator.Generate(500, seed: 1);
        var b = Generator.Generate(500, seed: 1);
        var c = Generator.Generate(500, seed: 2);

        a.Select(r => r.Label).ShouldBe(b.Select(r => r.Label));
        a[10].Numeric.ShouldBe(b[10].Numeric);
        a.Select(r => r.Strategy).ShouldBe(b.Select(r => r.Strategy));
        a.Select(r => r.Label).ShouldNotBe(c.Select(r => r.Label));
    }

    [Fact]
    public void The_positive_rate_is_about_forty_five_percent_and_true_probabilities_are_proper_and_vary()
    {
        var rows = Generator.Generate(40_000, seed: 3);

        rows.Average(r => r.Label ? 1.0 : 0.0).ShouldBe(0.45, 0.05);
        rows.ShouldAllBe(r => r.TrueProbability > 0 && r.TrueProbability < 1);
        rows.Max(r => r.TrueProbability).ShouldBeGreaterThan(0.8f);
        rows.Min(r => r.TrueProbability).ShouldBeLessThan(0.2f);
    }

    [Fact]
    public void The_label_follows_the_true_probability_so_the_optimal_auc_is_meaningful()
    {
        var rows = Generator.Generate(30_000, seed: 4);

        var optimal = Metrics.Auc([.. rows.Select(r => (double)r.TrueProbability)], [.. rows.Select(r => r.Label)]);

        optimal.ShouldBeInRange(0.70, 0.95);
    }

    [Fact]
    public void Rows_have_the_expected_shape()
    {
        var row = Generator.Generate(1, seed: 5)[0];

        row.Numeric.Length.ShouldBe(Row.NumericCount);
        new[] { row.Strategy, row.Timeframe, row.Side, row.Style, row.Regime, row.Weekday }.ShouldAllBe(s => s.Length > 0);
    }
}

public class MetricsTests
{
    [Fact]
    public void Auc_is_one_for_perfect_zero_for_inverted_and_half_for_ties()
    {
        bool[] labels = [false, false, true, true];

        Metrics.Auc([0.1, 0.2, 0.8, 0.9], labels).ShouldBe(1.0);
        Metrics.Auc([0.9, 0.8, 0.2, 0.1], labels).ShouldBe(0.0);
        Metrics.Auc([0.5, 0.5, 0.5, 0.5], labels).ShouldBe(0.5);
    }

    [Fact]
    public void Auc_matches_a_hand_computed_case()
    {
        // positives 0.7, 0.4 ; negatives 0.6, 0.3 → pairs won: (0.7>0.6, 0.7>0.3, 0.4>0.3) = 3 of 4
        Metrics.Auc([0.7, 0.6, 0.4, 0.3], [true, false, true, false]).ShouldBe(0.75, 1e-12);
    }

    [Fact]
    public void Log_loss_and_brier_match_hand_computed_values()
    {
        double[] p = [0.9, 0.2];
        bool[] y = [true, false];

        Metrics.LogLoss(p, y).ShouldBe((-Math.Log(0.9) - Math.Log(0.8)) / 2, 1e-12);
        Metrics.Brier(p, y).ShouldBe(((0.1 * 0.1) + (0.2 * 0.2)) / 2, 1e-12);
    }

    [Fact]
    public void Ece_is_zero_when_calibrated_and_large_when_overconfident()
    {
        var calibrated = Enumerable.Repeat(0.25, 4).ToArray();
        bool[] oneInFour = [true, false, false, false];
        var overconfident = Enumerable.Repeat(0.95, 4).ToArray();

        Metrics.Ece(calibrated, oneInFour).ShouldBe(0.0, 1e-12);
        Metrics.Ece(overconfident, oneInFour).ShouldBe(0.70, 1e-12);
    }

    [Fact]
    public void Percentile_and_median_use_the_nearest_rank()
    {
        Metrics.Median([4, 1, 3, 2, 5]).ShouldBe(3);
        Metrics.Percentile([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 0.95).ShouldBe(10);
        Metrics.Percentile([], 0.5).ShouldBe(0);
    }
}

public class PavTests
{
    private static (double[] Scores, bool[] Labels) Miscalibrated(int n)
    {
        // Scores are monotone in the true rate but badly scaled: true P = score², reported score = sqrt-ish.
        var random = new Random(9);
        var scores = new double[n];
        var labels = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var s = random.NextDouble();
            scores[i] = s;
            labels[i] = random.NextDouble() < s * s;
        }

        return (scores, labels);
    }

    [Fact]
    public void The_fitted_function_is_monotone_non_decreasing()
    {
        var (scores, labels) = Miscalibrated(5000);
        var pav = Pav.Fit(scores, labels);

        var outputs = Enumerable.Range(0, 200).Select(i => pav.Apply(i / 199.0)).ToArray();

        for (var i = 1; i < outputs.Length; i++)
        {
            outputs[i].ShouldBeGreaterThanOrEqualTo(outputs[i - 1]);
        }
    }

    [Fact]
    public void Calibration_error_drops_a_lot_on_a_deliberately_miscalibrated_set()
    {
        var (fitScores, fitLabels) = Miscalibrated(20_000);
        var (testScores, testLabels) = Miscalibrated(20_000);
        var pav = Pav.Fit(fitScores, fitLabels);

        var before = Metrics.Ece(testScores, testLabels);
        var after = Metrics.Ece([.. testScores.Select(pav.Apply)], testLabels);

        before.ShouldBeGreaterThan(0.08);
        after.ShouldBeLessThan(0.03);
    }

    [Fact]
    public void Ties_and_a_single_class_do_not_break_the_fit()
    {
        var pav = Pav.Fit([0.5, 0.5, 0.5], [true, false, true]);

        pav.Apply(0.5).ShouldBe(2.0 / 3, 1e-12);
        pav.Apply(0.9).ShouldBe(2.0 / 3, 1e-12);
        Pav.Fit([0.1, 0.2], [false, false]).Apply(0.15).ShouldBe(0.0);
    }
}

public class EndToEndTests
{
    private static SpikeReport Run(string trainer) => new Spike(new SpikeOptions("unit-test", trainer, 6000, 2, 10, 7, 1, Path.Combine(Path.GetTempPath(), "xd-mlspike-tests")), _ => { }).Run();

    [Fact]
    public void FastTree_trains_calibrates_explains_and_round_trips_the_model()
    {
        var result = Run("fasttree").Results.Single();

        result.Auc.ShouldBeGreaterThan(0.6);
        result.AucOptimal.ShouldBeGreaterThan(result.Auc - 0.02);
        result.SaveLoadMaxDelta.ShouldBeLessThanOrEqualTo(1e-6);
        result.TopContributions.Count.ShouldBeGreaterThan(0);
        result.TopPermutationImportance.Count.ShouldBeGreaterThan(0);
        result.Ece.OwnPav.ShouldBeLessThan(0.15);
        result.BatchRowsPerSecond.ShouldBeGreaterThan(0);
        result.SingleRowP95Ms.ShouldBeGreaterThanOrEqualTo(result.SingleRowP50Ms);
    }

    [Fact]
    public void LightGbm_does_the_same_where_its_native_library_exists()
    {
        SpikeReport report;
        try
        {
            report = Run("lightgbm");
        }
        catch (DllNotFoundException ex)
        {
            Assert.Skip($"LightGBM native library is not available on this platform: {ex.Message.Split('\n')[0]}");
            return;
        }

        report.Results.Single().Auc.ShouldBeGreaterThan(0.6);
        report.Results.Single().SaveLoadMaxDelta.ShouldBeLessThanOrEqualTo(1e-6);
    }
}
