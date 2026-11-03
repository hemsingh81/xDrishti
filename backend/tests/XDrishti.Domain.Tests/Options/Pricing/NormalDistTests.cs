using XDrishti.Domain.Options.Pricing;

namespace XDrishti.Domain.Tests.Options.Pricing;

public sealed class NormalDistTests
{
    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(1.96, 0.9750021048517795)]
    [InlineData(0.5, 0.691462461274013)]
    [InlineData(-1.0, 0.15865525393145705)]
    [InlineData(-2.9, 0.0018658133003840378)]
    [InlineData(2.99, 0.9986051127645077)]
    [InlineData(3.0, 0.9986501019683699)]
    [InlineData(-3.0, 0.0013498980316300957)]
    [InlineData(3.5, 0.9997673709209645)]
    [InlineData(-5.0, 2.866515718791945e-07)]
    [InlineData(5.0, 0.9999997133484281)]
    [InlineData(-8.0, 6.22096057427182e-16)]
    public void Cdf_matches_tabled_values_to_1e_14(double x, double expected)
    {
        NormalDist.Cdf(x).ShouldBe(expected, 1e-14);
    }

    [Fact]
    public void Cdf_keeps_a_small_relative_error_in_the_far_tail()
    {
        (NormalDist.Cdf(-8.0) / 6.22096057427182e-16).ShouldBe(1.0, 1e-12);
        (NormalDist.Cdf(-5.0) / 2.866515718791945e-07).ShouldBe(1.0, 1e-12);
    }

    [Theory]
    [InlineData(38.0)]
    [InlineData(100.0)]
    [InlineData(double.PositiveInfinity)]
    public void Cdf_is_exactly_one_from_38_up(double x)
    {
        NormalDist.Cdf(x).ShouldBe(1.0);
    }

    [Theory]
    [InlineData(-38.0)]
    [InlineData(-100.0)]
    [InlineData(double.NegativeInfinity)]
    public void Cdf_is_exactly_zero_from_minus_38_down(double x)
    {
        NormalDist.Cdf(x).ShouldBe(0.0);
    }

    [Fact]
    public void Cdf_is_symmetric_and_non_decreasing_across_the_series_and_fraction_switch()
    {
        var previous = -1.0;
        for (var x = -10.0; x <= 10.0; x += 0.0125)
        {
            var value = NormalDist.Cdf(x);
            value.ShouldBeGreaterThanOrEqualTo(previous);
            (value + NormalDist.Cdf(-x)).ShouldBe(1.0, 1e-15);
            previous = value;
        }
    }

    [Fact]
    public void Pdf_is_the_standard_normal_density()
    {
        NormalDist.Pdf(0.0).ShouldBe(0.3989422804014327, 1e-16);
        NormalDist.Pdf(1.0).ShouldBe(0.24197072451914337, 1e-16);
    }
}
