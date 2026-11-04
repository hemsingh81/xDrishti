using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

public sealed class NormalDistributionTests
{
    [Theory]
    [InlineData(0.95, 1.6449)]
    [InlineData(0.80, 0.8416)]
    [InlineData(0.975, 1.95996)]
    [InlineData(0.5, 0.0)]
    [InlineData(0.05, -1.6449)]
    [InlineData(0.999, 3.0902)]
    public void The_quantile_matches_the_table(double p, double z) => NormalDistribution.Quantile(p).ShouldBe(z, 1e-4);

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(1.96, 0.975)]
    [InlineData(-1.0, 0.158655)]
    public void The_cdf_matches_the_table(double x, double p) => NormalDistribution.Cdf(x).ShouldBe(p, 1e-5);

    [Fact]
    public void The_functions_hold_to_eight_digits_including_the_tails()
    {
        NormalDistribution.Quantile(0.975).ShouldBe(1.959963984540054, 1e-8);
        NormalDistribution.Quantile(0.999).ShouldBe(3.090232306167813, 1e-8);
        NormalDistribution.Quantile(0.95).ShouldBe(1.644853626951472, 1e-8);
        NormalDistribution.Cdf(1.96).ShouldBe(0.9750021048517795, 1e-12);
        NormalDistribution.Cdf(-6).ShouldBe(9.865876450376946e-10, 1e-15);
        NormalDistribution.Cdf(-3).ShouldBe(0.0013498980316301, 1e-12);
    }

    [Fact]
    public void The_quantile_inverts_the_cdf_across_the_range()
    {
        for (var p = 0.001; p < 1; p += 0.0173)
        {
            NormalDistribution.Cdf(NormalDistribution.Quantile(p)).ShouldBe(p, 1e-7);
        }
    }

    [Fact]
    public void The_ends_are_the_infinities_and_a_bad_probability_throws()
    {
        NormalDistribution.Quantile(0).ShouldBe(double.NegativeInfinity);
        NormalDistribution.Quantile(1).ShouldBe(double.PositiveInfinity);
        Should.Throw<ArgumentOutOfRangeException>(() => NormalDistribution.Quantile(1.1));
        Should.Throw<ArgumentOutOfRangeException>(() => NormalDistribution.Quantile(double.NaN));
        double.IsNaN(NormalDistribution.Cdf(double.NaN)).ShouldBeTrue();
    }
}
