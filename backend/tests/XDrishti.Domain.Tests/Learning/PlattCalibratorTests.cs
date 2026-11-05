using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Platt scaling by hand tables (CF-10 part 10a, ADR 0214): smoothed targets, the deterministic Newton fit, monotone and two parameters.</summary>
public sealed class PlattCalibratorTests
{
    [Fact]
    public void A_constant_score_with_three_wins_in_eight_gives_the_mean_of_the_smoothed_targets_and_isotonic_gives_three_eighths()
    {
        double[] scores = [1, 1, 1, 1, 1, 1, 1, 1];
        int[] labels = [1, 1, 1, 0, 0, 0, 0, 0];

        var platt = PlattCalibrator.Fit(scores, labels);

        // targets: wins (3 + 1) / (3 + 2) = 0.8, losses 1 / (5 + 2) = 1/7; the best constant is their mean (3 x 0.8 + 5 / 7) / 8
        Math.Round(platt.Apply(1), 6).ShouldBe(0.389286);
        Math.Round(platt.Apply(-40), 6).ShouldBe(0.389286);
        Math.Round(IsotonicCalibrator.Fit(scores, labels).Apply(1), 6).ShouldBe(0.375);
        platt.Slope.ShouldBe(0);
    }

    [Fact]
    public void Two_rows_fit_by_hand_a_slope_of_ln_2_and_the_targets_two_thirds_and_one_third()
    {
        // scores -1 and +1, labels 0 and 1: targets 1/3 and 2/3, centre 0, spread 1; the gradient is zero where p(+1) = 2/3, that is slope = ln 2
        var platt = PlattCalibrator.Fit([-1, 1], [0, 1]);

        Math.Round(platt.Slope, 6).ShouldBe(Math.Round(Math.Log(2), 6));
        Math.Round(platt.Intercept, 6).ShouldBe(0);
        Math.Round(platt.Center, 6).ShouldBe(0);
        Math.Round(platt.Apply(1), 6).ShouldBe(Math.Round(2.0 / 3, 6));
        Math.Round(platt.Apply(-1), 6).ShouldBe(Math.Round(1.0 / 3, 6));
    }

    [Fact]
    public void Separable_scores_give_a_finite_fit_because_the_targets_are_smoothed()
    {
        var platt = PlattCalibrator.Fit([1, 2, 3, 4, 5, 6], [0, 0, 0, 1, 1, 1]);

        double.IsFinite(platt.Slope).ShouldBeTrue();
        platt.Slope.ShouldBeGreaterThan(0);
        platt.Apply(6).ShouldBeLessThan(1);
        platt.Apply(1).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_map_never_falls_as_the_score_rises_and_a_falling_relation_gives_the_best_constant()
    {
        var rising = PlattCalibrator.Fit([1, 2, 3, 4, 5, 6, 7, 8], [0, 1, 0, 0, 1, 1, 0, 1]);
        var falling = PlattCalibrator.Fit([1, 2, 3, 4, 5, 6], [1, 1, 1, 0, 0, 0]);

        var probes = Enumerable.Range(-20, 60).Select(i => i / 2.0).ToArray();
        probes.Select(rising.Apply).ShouldBe(probes.Select(rising.Apply).Order());
        falling.Slope.ShouldBe(0);
        Math.Round(falling.Apply(3), 6).ShouldBe(0.5);
    }

    [Fact]
    public void The_same_input_gives_the_same_parameters_twice_and_a_permuted_input_the_same_within_rounding()
    {
        double[] scores = [0.3, -1.2, 0.8, 2.1, -0.4, 1.5, 0.0, -2.0];
        int[] labels = [1, 0, 1, 1, 0, 1, 0, 0];
        int[] order = [5, 2, 7, 0, 3, 6, 1, 4];

        var a = PlattCalibrator.Fit(scores, labels);
        var b = PlattCalibrator.Fit(scores, labels);
        var permuted = PlattCalibrator.Fit([.. order.Select(i => scores[i])], [.. order.Select(i => labels[i])]);

        (b.Slope, b.Center, b.Intercept).ShouldBe((a.Slope, a.Center, a.Intercept));
        permuted.Slope.ShouldBe(a.Slope, 1e-9);
        permuted.Center.ShouldBe(a.Center, 1e-9);
        permuted.Intercept.ShouldBe(a.Intercept, 1e-9);
    }

    [Fact]
    public void A_fit_on_labels_drawn_from_a_sigmoid_recovers_its_direction_and_rough_size()
    {
        // expected labels, no sampling: at each score the fraction of wins is the true probability (20 rows per score)
        var scores = new List<double>();
        var labels = new List<int>();
        foreach (var s in new[] { -2.0, -1.0, 0.0, 1.0, 2.0 })
        {
            var wins = (int)Math.Round(20 / (1 + Math.Exp(-(1.2 * s))));
            for (var i = 0; i < 20; i++)
            {
                scores.Add(s);
                labels.Add(i < wins ? 1 : 0);
            }
        }

        var platt = PlattCalibrator.Fit(scores, labels);

        platt.Slope.ShouldBeInRange(1.0, 1.4);
        platt.Apply(0).ShouldBeInRange(0.45, 0.55);
    }

    [Fact]
    public void Non_finite_scores_are_skipped_and_nothing_usable_gives_one_half()
    {
        PlattCalibrator.Fit([double.NaN, double.PositiveInfinity], [1, 0]).Apply(3).ShouldBe(0.5, 1e-12);
        PlattCalibrator.Fit([double.NaN, -1, 1], [1, 0, 1]).Slope.ShouldBeGreaterThan(0);
        PlattCalibrator.Fit([-1, 1], [0, 1]).Apply(double.NaN).ShouldBe(0.5, 1e-9);
    }

    [Fact]
    public void Stored_parameters_must_be_finite_and_the_slope_not_negative()
    {
        PlattCalibrator.FromParameters(0.5, 1, -0.2).Apply(1).ShouldBe(1 / (1 + Math.Exp(0.2)), 1e-12);
        Should.Throw<ArgumentException>(() => PlattCalibrator.FromParameters(-0.1, 0, 0));
        Should.Throw<ArgumentException>(() => PlattCalibrator.FromParameters(double.NaN, 0, 0));
        Should.Throw<ArgumentException>(() => PlattCalibrator.Fit([1, 2], [1]));
    }
}
