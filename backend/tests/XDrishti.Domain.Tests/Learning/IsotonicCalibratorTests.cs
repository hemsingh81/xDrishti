using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The pool-adjacent-violators map by hand tables (P6-06, ADR 0068).</summary>
public sealed class IsotonicCalibratorTests
{
    [Fact]
    public void Scores_that_already_rise_with_the_labels_pool_only_equal_neighbours()
    {
        var map = IsotonicCalibrator.Fit([1, 2, 3, 4], [0, 0, 1, 1]);

        map.Scores.ShouldBe([1.5, 3.5]);
        map.Values.ShouldBe([0.0, 1.0]);
    }

    [Fact]
    public void A_violation_is_pooled_into_one_block_with_the_mean_of_its_labels()
    {
        // scores 1..5 with labels 0,1,0,1,1: the 1,0 pair (scores 2 and 3) is pooled to 0.5, and the two 1s at the top (equal means) are one block
        var map = IsotonicCalibrator.Fit([1, 2, 3, 4, 5], [0, 1, 0, 1, 1]);

        map.Scores.ShouldBe([1.0, 2.5, 4.5]);
        map.Values.ShouldBe([0.0, 0.5, 1.0]);
    }

    [Fact]
    public void Pooling_cascades_backwards()
    {
        // labels 1,1,0,0 on rising scores pool everything to 0.5
        var map = IsotonicCalibrator.Fit([1, 2, 3, 4], [1, 1, 0, 0]);

        map.Values.ShouldBe([0.5]);
        map.Scores.ShouldBe([2.5]);
    }

    [Fact]
    public void Equal_scores_are_one_point_whatever_the_order_given()
    {
        var map = IsotonicCalibrator.Fit([2, 1, 2, 1], [1, 0, 0, 1]);

        map.Scores.ShouldBe([1.5]);
        map.Values.ShouldBe([0.5]);
    }

    [Fact]
    public void Apply_interpolates_between_knots_and_is_flat_outside()
    {
        var map = IsotonicCalibrator.FromKnots([0, 10], [0.2, 0.6]);

        map.Apply(5).ShouldBe(0.4, 1e-12);
        map.Apply(-3).ShouldBe(0.2);
        map.Apply(99).ShouldBe(0.6);
        map.Apply(10).ShouldBe(0.6);
        map.Apply(double.NaN).ShouldBe(0.6, "a non-finite score reads as the middle knot (index 1 of 2)");
    }

    [Fact]
    public void The_fitted_map_never_falls_for_noisy_data_and_the_same_input_gives_the_same_knots()
    {
        var random = new Random(3);
        var scores = Enumerable.Range(0, 400).Select(_ => random.NextDouble()).ToArray();
        var labels = scores.Select(s => random.NextDouble() < (0.2 + (0.5 * s)) ? 1 : 0).ToArray();

        var first = IsotonicCalibrator.Fit(scores, labels);
        var second = IsotonicCalibrator.Fit(scores, labels);

        first.Values.ShouldBe(first.Values.Order());
        first.Scores.ShouldBe(first.Scores.Order());
        second.Scores.ShouldBe(first.Scores);
        second.Values.ShouldBe(first.Values);
        Enumerable.Range(0, 100).Select(i => first.Apply(i / 100.0)).ShouldBe(Enumerable.Range(0, 100).Select(i => first.Apply(i / 100.0)).Order());
    }

    [Fact]
    public void Non_finite_scores_are_skipped_and_nothing_usable_gives_one_half()
    {
        IsotonicCalibrator.Fit([double.NaN, 1], [1, 0]).Values.ShouldBe([0.0]);
        IsotonicCalibrator.Fit([double.NaN], [1]).Values.ShouldBe([0.5]);
        IsotonicCalibrator.Fit([], []).Apply(3).ShouldBe(0.5);
    }

    [Fact]
    public void Bad_input_is_refused()
    {
        Should.Throw<ArgumentException>(() => IsotonicCalibrator.Fit([1, 2], [1]));
        Should.Throw<ArgumentException>(() => IsotonicCalibrator.FromKnots([], []));
        Should.Throw<ArgumentException>(() => IsotonicCalibrator.FromKnots([1, 1], [0.1, 0.2]));
        Should.Throw<ArgumentException>(() => IsotonicCalibrator.FromKnots([1, 2], [0.5, 0.4]));
        Should.Throw<ArgumentException>(() => IsotonicCalibrator.FromKnots([1, 2], [0.5, 1.5]));
    }
}
