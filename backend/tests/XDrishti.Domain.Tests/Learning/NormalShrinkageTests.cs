using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-02 acceptance criteria 1 and 8 and the property tests of the normal-normal posterior.</summary>
public sealed class NormalShrinkageTests
{
    [Fact]
    public void The_research_example_with_10_trades_stays_below_zero()
    {
        var p = NormalShrinkage.Posterior(0.40, 1.44, 10, 0.10, 0.04, 0.05)!.Value;

        p.Blend.ShouldBe(0.2174, 1e-3);
        p.Mean.ShouldBe(0.1652, 1e-3);
        p.Variance.ShouldBe(0.03130, 1e-4);
        Math.Sqrt(p.Variance).ShouldBe(0.1769, 1e-3);
        p.Lower.ShouldBe(-0.1258, 1e-3);
    }

    [Fact]
    public void The_research_example_with_40_trades_clears_zero()
    {
        var p = NormalShrinkage.Posterior(0.40, 1.44, 40, 0.10, 0.04, 0.05)!.Value;

        p.Blend.ShouldBe(0.5263, 1e-3);
        p.Mean.ShouldBe(0.2579, 1e-3);
        p.Variance.ShouldBe(0.01895, 1e-4);
        p.Lower.ShouldBe(0.0315, 1e-3);
    }

    [Fact]
    public void The_acceptance_example_with_positive_tau_squared()
    {
        // AC 4: cell 1, parent 0.0870, tau^2 0.0728, sigma^2 1.44, n 10, mean 0.60
        var p = NormalShrinkage.Posterior(0.60, 1.44, 10, 10.0 / 115, 0.0728, 0.05)!.Value;

        p.Blend.ShouldBe(0.3359, 1e-3);
        p.Mean.ShouldBe(0.2593, 1e-3);
        p.Variance.ShouldBe(0.04837, 1e-4);
        p.Lower.ShouldBe(-0.1025, 1e-3);
    }

    [Fact]
    public void The_floor_keeps_the_cells_apart_where_tau_squared_would_be_zero()
    {
        // AC 3: floor 0.01, n 10, mean 0.40, leave-one-out parent 0.1065
        var p = NormalShrinkage.Posterior(0.40, 1.44, 10, 0.1065, 0.01, 0.05)!.Value;

        p.Blend.ShouldBe(0.0649, 1e-3);
        p.Mean.ShouldBe(0.1256, 1e-3);
        NormalShrinkage.Posterior(0.40, 1.44, 10, 0.1065, 0, 0.05)!.Value.Mean.ShouldBe(0.1065, 1e-12);
    }

    [Fact]
    public void No_trade_says_nothing_so_the_mean_is_the_parents_and_the_spread_is_tau()
    {
        var p = NormalShrinkage.Posterior(double.NaN, 1.44, 0, 0.10, 0.04, 0.05)!.Value;

        p.Mean.ShouldBe(0.10);
        p.Blend.ShouldBe(0);
        p.Variance.ShouldBe(0.04, 1e-12);
        p.Lower.ShouldBe(0.10 - (1.6449 * 0.2), 1e-3);
    }

    [Theory]
    [InlineData(double.NaN, 1.44, 10, 0.1, 0.04, 0.05)]
    [InlineData(0.4, double.PositiveInfinity, 10, 0.1, 0.04, 0.05)]
    [InlineData(0.4, 0.0, 10, 0.1, 0.04, 0.05)]
    [InlineData(0.4, 1.44, -1, 0.1, 0.04, 0.05)]
    [InlineData(0.4, 1.44, double.NaN, 0.1, 0.04, 0.05)]
    [InlineData(0.4, 1.44, 10, double.NaN, 0.04, 0.05)]
    [InlineData(0.4, 1.44, 10, 0.1, -0.01, 0.05)]
    [InlineData(0.4, 1.44, 10, 0.1, double.NaN, 0.05)]
    [InlineData(0.4, 1.44, 10, 0.1, 0.04, 0.0)]
    [InlineData(0.4, 1.44, 10, 0.1, 0.04, 0.5)]
    public void An_input_that_is_not_usable_gives_no_number(double m, double sigma2, double n, double parent, double tau2, double level) =>
        NormalShrinkage.Posterior(m, sigma2, n, parent, tau2, level).ShouldBeNull();

    [Fact]
    public void Blend_is_in_0_1_the_mean_lies_between_raw_and_parent_and_a_larger_n_blends_more()
    {
        var rng = new Random(11);
        for (var k = 0; k < 500; k++)
        {
            var m = (rng.NextDouble() - 0.5) * 4;
            var parent = (rng.NextDouble() - 0.5) * 2;
            var sigma2 = 0.1 + (rng.NextDouble() * 4);
            var tau2 = rng.NextDouble() * 0.5;
            var n = 1 + rng.Next(200);

            var p = NormalShrinkage.Posterior(m, sigma2, n, parent, tau2, 0.05)!.Value;
            var larger = NormalShrinkage.Posterior(m, sigma2, n + 5, parent, tau2, 0.05)!.Value;

            p.Blend.ShouldBeInRange(0, 1);
            p.Mean.ShouldBeInRange(Math.Min(m, parent) - 1e-9, Math.Max(m, parent) + 1e-9);
            larger.Blend.ShouldBeGreaterThanOrEqualTo(p.Blend);
            p.Lower.ShouldBeLessThanOrEqualTo(p.Mean);
        }
    }

    [Fact]
    public void Doubling_R_doubles_the_shrunk_mean_and_quadruples_the_variances()
    {
        var one = NormalShrinkage.Posterior(0.4, 1.44, 12, 0.1, 0.04, 0.05)!.Value;
        var two = NormalShrinkage.Posterior(0.8, 4 * 1.44, 12, 0.2, 4 * 0.04, 0.05)!.Value;

        two.Mean.ShouldBe(2 * one.Mean, 1e-12);
        two.Lower.ShouldBe(2 * one.Lower, 1e-12);
        two.Blend.ShouldBe(one.Blend, 1e-12);
        two.Variance.ShouldBe(4 * one.Variance, 1e-12);
    }
}
