using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-03 acceptance criteria 1 to 4 (hand-worked, ADR 0205) and the properties of the deflated Sharpe ratio.</summary>
public sealed class DeflatedSharpeTests
{
    [Fact]
    public void Expected_max_of_sixteen_trials_with_annual_spread_half_is_point_nine()
    {
        // 0.4228 x 1.5341 + 0.5772 x 1.9955 = 1.8004; x 0.5 = 0.9002
        DeflatedSharpe.ExpectedMax(16, 0.5).ShouldBe(0.9002, 5e-4);
    }

    [Fact]
    public void Expected_max_per_trade_with_spread_point_oh_five_is_point_oh_nine()
    {
        DeflatedSharpe.ExpectedMax(16, 0.05).ShouldBe(0.0900, 5e-5);
    }

    [Fact]
    public void A_candidate_at_the_bar_has_probability_one_half_and_below_it_less()
    {
        var bar = DeflatedSharpe.ExpectedMax(16, 0.5);

        DeflatedSharpe.Probabilistic(bar, bar, 100, 0, 3).Value!.Value.ShouldBe(0.5, 1e-12);
        DeflatedSharpe.Probabilistic(0.85, bar, 100, 0, 3).Value!.Value.ShouldBeLessThan(0.5);
    }

    [Theory]
    [InlineData(0.15, 100, 0, 0.7236)]
    [InlineData(0.25, 100, 0, 0.9415)]
    [InlineData(0.15, 100, -1, 0.7102)]
    [InlineData(0.15, 30, 0, 0.626)]
    public void The_per_trade_worked_cases(double sr, double t, double skew, double expected)
    {
        var dsr = DeflatedSharpe.Deflated(sr, 16, 0.05, t, skew, 3);

        dsr.Unavailable.ShouldBeNull();
        dsr.Value!.Value.ShouldBe(expected, 5e-4);
    }

    [Fact]
    public void Neither_worked_case_reaches_the_bar_of_ninety_five_percent()
    {
        DeflatedSharpe.Deflated(0.15, 16, 0.05, 100, 0, 3).Value!.Value.ShouldBeLessThan(0.95);
        DeflatedSharpe.Deflated(0.25, 16, 0.05, 100, 0, 3).Value!.Value.ShouldBeLessThan(0.95);
    }

    [Fact]
    public void One_trial_has_no_luck_bar_and_is_the_probabilistic_sharpe_ratio_against_zero()
    {
        DeflatedSharpe.ExpectedMax(1, 0.2).ShouldBe(0);

        var one = DeflatedSharpe.Deflated(0.15, 1, double.NaN, 100, 0, 3);

        one.Value.ShouldBe(DeflatedSharpe.Probabilistic(0.15, 0, 100, 0, 3).Value);
    }

    [Theory]
    [InlineData(double.NaN, 100, 0, 3, "the Sharpe ratio")]
    [InlineData(0.15, 19, 0, 3, "fewer than 20 effective trades")]
    [InlineData(0.15, 100, double.NaN, 3, "skew or kurtosis")]
    [InlineData(0.15, 100, 0, double.NaN, "skew or kurtosis")]
    [InlineData(2.0, 100, 5, 3, "not positive")]
    public void Unavailable_inputs_give_a_reason_and_never_a_number(double sr, double t, double skew, double kurt, string reason)
    {
        var dsr = DeflatedSharpe.Deflated(sr, 16, 0.05, t, skew, kurt);

        dsr.Value.ShouldBeNull();
        dsr.Unavailable.ShouldNotBeNull().ShouldStartWith("unavailable: ");
        dsr.Unavailable.ShouldContain(reason);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    [InlineData(double.PositiveInfinity)]
    public void An_unusable_trial_spread_is_unavailable_for_several_trials(double sigma)
    {
        DeflatedSharpe.Deflated(0.15, 16, sigma, 100, 0, 3).Unavailable.ShouldNotBeNull().ShouldContain("spread");
        DeflatedSharpe.ExpectedMax(16, sigma).ShouldBeNaN();
    }

    [Fact]
    public void No_trial_is_unavailable()
    {
        DeflatedSharpe.Deflated(0.15, 0, 0.05, 100, 0, 3).Unavailable.ShouldNotBeNull();
        DeflatedSharpe.ExpectedMax(0, 0.05).ShouldBeNaN();
    }

    [Fact]
    public void The_probability_rises_with_the_ratio_and_the_trades_and_falls_with_the_trials_and_the_spread()
    {
        double P(double sr = 0.2, int n = 16, double sigma = 0.05, double t = 100) => DeflatedSharpe.Deflated(sr, n, sigma, t, 0, 3).Value!.Value;

        P(sr: 0.25).ShouldBeGreaterThan(P(sr: 0.20));
        P(t: 200).ShouldBeGreaterThan(P(t: 100));
        P(n: 32).ShouldBeLessThan(P(n: 16));
        P(sigma: 0.08).ShouldBeLessThan(P(sigma: 0.05));
    }

    [Fact]
    public void The_expected_max_grows_with_the_trials()
    {
        DeflatedSharpe.ExpectedMax(64, 0.05).ShouldBeGreaterThan(DeflatedSharpe.ExpectedMax(16, 0.05));
        DeflatedSharpe.ExpectedMax(2, 0.05).ShouldBeGreaterThan(0);
    }
}
