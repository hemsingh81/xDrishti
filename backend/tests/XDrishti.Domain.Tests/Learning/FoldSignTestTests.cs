using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10c-1 (ADR 0216), spec AC6: the fold sign test.</summary>
public sealed class FoldSignTestTests
{
    [Fact]
    public void Five_of_six_folds_better_is_7_over_64()
    {
        var r = FoldSignTest.Assess([0.1, 0.2, 0.3, 0.1, 0.2, -0.1]);

        (r.Better, r.Worse, r.Ties, r.Folds).ShouldBe((5, 1, 0, 6));
        r.PValue.ShouldBe(7.0 / 64); // 0.109375
    }

    [Fact]
    public void Six_of_six_is_1_over_64()
    {
        FoldSignTest.Assess([1, 2, 3, 4, 5, 6]).PValue.ShouldBe(0.015625);
    }

    [Theory]
    [InlineData(0, 6, 1.0)]
    [InlineData(3, 6, 42.0 / 64)] // 20 + 15 + 6 + 1
    [InlineData(4, 6, 22.0 / 64)] // 15 + 6 + 1
    [InlineData(1, 1, 0.5)]
    public void The_tail_is_the_exact_binomial_at_one_half(int better, int folds, double expected)
    {
        FoldSignTest.PValue(better, folds).ShouldBe(expected);
    }

    [Fact]
    public void A_fold_with_a_gain_of_exactly_zero_is_left_out()
    {
        var r = FoldSignTest.Assess([0.1, 0.0, 0.2, -0.1]);

        (r.Better, r.Worse, r.Ties).ShouldBe((2, 1, 1));
        r.PValue.ShouldBe(0.5); // 2 or more of 3: (3 + 1) / 8
    }

    [Fact]
    public void No_fold_that_counts_gives_no_p_value_and_never_a_pass()
    {
        FoldSignTest.Assess([]).PValue.ShouldBeNull();
        FoldSignTest.Assess([0.0, 0.0]).PValue.ShouldBeNull();
        FoldSignTest.PValue(0, 1001).ShouldBeNull();
    }

    [Fact]
    public void The_tail_is_finite_and_a_probability_for_the_largest_count()
    {
        var p = FoldSignTest.PValue(500, 1000)!.Value;

        p.ShouldBeInRange(0.5, 0.52);
        (FoldSignTest.PValue(1000, 1000)!.Value / Math.ScaleB(1.0, -1000)).ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void More_better_folds_than_folds_is_a_caller_error()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => FoldSignTest.PValue(7, 6));
    }
}
