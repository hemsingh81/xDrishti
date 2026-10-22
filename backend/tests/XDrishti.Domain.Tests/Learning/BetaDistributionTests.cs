using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The Beta functions against closed forms (P6-01).</summary>
public sealed class BetaDistributionTests
{
    [Theory]
    [InlineData(0.3, 1, 1, 0.3)]
    [InlineData(0.5, 2, 1, 0.25)]
    [InlineData(0.5, 1, 2, 0.75)]
    [InlineData(0.5, 3, 3, 0.5)]
    [InlineData(0.2, 2, 2, 0.104)]
    public void Cdf_matches_the_closed_form(double x, double a, double b, double expected)
    {
        BetaDistribution.Cdf(x, a, b).ShouldBe(expected, 1e-9);
    }

    [Fact]
    public void Cdf_is_clamped_outside_the_unit_interval()
    {
        BetaDistribution.Cdf(-1, 2, 3).ShouldBe(0);
        BetaDistribution.Cdf(2, 2, 3).ShouldBe(1);
    }

    [Theory]
    [InlineData(0.05, 1, 1, 0.05)]
    [InlineData(0.05, 2, 1, 0.2236067977)]
    [InlineData(0.05, 1, 2, 0.0253205655)]
    [InlineData(0.5, 5, 5, 0.5)]
    public void Quantile_matches_the_closed_form(double p, double a, double b, double expected)
    {
        BetaDistribution.Quantile(p, a, b).ShouldBe(expected, 1e-8);
    }

    [Fact]
    public void Bad_arguments_throw()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BetaDistribution.Cdf(0.5, 0, 1));
        Should.Throw<ArgumentOutOfRangeException>(() => BetaDistribution.Quantile(1.5, 1, 1));
        BetaDistribution.Quantile(0, 1, 1).ShouldBe(0);
        BetaDistribution.Quantile(1, 1, 1).ShouldBe(1);
    }
}
