using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-03: the moments helper (sample sd, population-moment skew and kurtosis, Sharpe ratio per trade).</summary>
public sealed class TradeMomentsTests
{
    [Fact]
    public void Hand_case_of_four_results()
    {
        // 1, 2, 3, 6: mean 3, deviations -2 -1 0 3, m2 = 14/4 = 3.5, sd (n-1) = sqrt(14/3), m3 = (-8 -1 + 27)/4 = 4.5, m4 = (16 + 1 + 81)/4 = 24.5
        var m = TradeMoments.Of([1, 2, 3, 6]);

        m.N.ShouldBe(4);
        m.Mean.ShouldBe(3);
        m.Sd.ShouldBe(Math.Sqrt(14.0 / 3), 1e-12);
        m.Skew.ShouldBe(4.5 / Math.Pow(3.5, 1.5), 1e-12);
        m.Kurt.ShouldBe(24.5 / (3.5 * 3.5), 1e-12);
        m.Sr.ShouldBe(3 / Math.Sqrt(14.0 / 3), 1e-12);
    }

    [Fact]
    public void No_spread_gives_no_ratio_and_no_shape()
    {
        var m = TradeMoments.Of([1, 1, 1, 1, 1]);

        m.Sr.ShouldBeNaN();
        m.Skew.ShouldBeNaN();
        m.Kurt.ShouldBeNaN();
    }

    [Fact]
    public void Few_results_give_what_they_can()
    {
        TradeMoments.Of([]).Mean.ShouldBeNaN();
        TradeMoments.Of([2]).Sr.ShouldBeNaN();
        var three = TradeMoments.Of([1, 2, 4]);
        three.Sr.ShouldBeGreaterThan(0);
        three.Skew.ShouldBeNaN();
    }

    [Fact]
    public void A_result_that_is_not_finite_gives_nothing()
    {
        TradeMoments.Of([1, double.NaN, 3, 4]).Sr.ShouldBeNaN();
        TradeMoments.Of([1, double.PositiveInfinity, 3, 4]).Sr.ShouldBeNaN();
    }
}
