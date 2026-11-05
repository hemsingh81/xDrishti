using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10b (ADR 0215), spec AC8: a feature whose direction flips in more than one third of the folds is dropped.</summary>
public sealed class FeatureStabilityTests
{
    [Fact]
    public void Six_folds_with_three_flips_drop_the_feature()
    {
        var row = FeatureStability.Judge(4, [1, 1, 1, -1, -1, -1], 1.0 / 3.0);

        row.Flips.ShouldBe(3);
        row.Judged.ShouldBe(6);
        row.Verdict.ShouldBe(StabilityVerdict.Unstable);
        row.Index.ShouldBe(4);
    }

    [Fact]
    public void Six_folds_with_two_flips_keep_it()
    {
        var row = FeatureStability.Judge(0, [1, -1, 1, 1, -1, 1], 1.0 / 3.0);

        row.Flips.ShouldBe(2);
        row.Verdict.ShouldBe(StabilityVerdict.Stable);
    }

    [Fact]
    public void A_fold_without_a_direction_is_not_counted_either_way()
    {
        var row = FeatureStability.Judge(0, [1, 1, 0, 0, 1, -1], 1.0 / 3.0);

        row.Judged.ShouldBe(4);
        row.Flips.ShouldBe(1);
        row.Verdict.ShouldBe(StabilityVerdict.Stable);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 0, 0, 0, 1 })]
    public void Fewer_than_two_directions_is_unavailable_and_never_stable(int[] signs)
    {
        FeatureStability.Judge(0, signs, 1.0 / 3.0).Verdict.ShouldBe(StabilityVerdict.Unavailable);
    }

    [Fact]
    public void The_direction_is_the_sign_of_the_correlation_of_value_and_contribution()
    {
        double[] values = [.. Enumerable.Range(0, 40).Select(i => (double)i)];

        FeatureStability.Direction(values, [.. values.Select(v => (2 * v) + 1)], 30).ShouldBe(1);
        FeatureStability.Direction(values, [.. values.Select(v => -v)], 30).ShouldBe(-1);
    }

    [Fact]
    public void A_constant_side_too_few_rows_or_non_finite_pairs_give_no_direction()
    {
        double[] values = [.. Enumerable.Range(0, 40).Select(i => (double)i)];

        FeatureStability.Direction(values, [.. values.Select(_ => 1.0)], 30).ShouldBe(0);
        FeatureStability.Direction([.. values.Select(_ => 1.0)], values, 30).ShouldBe(0);
        FeatureStability.Direction(values.Take(10).ToArray(), values.Take(10).ToArray(), 30).ShouldBe(0);
        var holes = values.Select((v, i) => i < 20 ? double.NaN : v).ToArray();
        FeatureStability.Direction(holes, values, 30).ShouldBe(0);
        FeatureStability.Direction(holes, values, 20).ShouldBe(1);
    }

    [Fact]
    public void Nonsense_input_is_refused()
    {
        Should.Throw<ArgumentException>(() => FeatureStability.Direction([1, 2], [1], 2));
        Should.Throw<ArgumentOutOfRangeException>(() => FeatureStability.Judge(0, [1, 1], 0));
        Should.Throw<ArgumentOutOfRangeException>(() => FeatureStability.Judge(0, [1, 1], 1));
    }
}
