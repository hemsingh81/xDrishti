using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-06, D5 and AC10, worked by hand with development OOS +0.20 R, 30 trades needed, 50 % kept.</summary>
public sealed class HoldOutRuleTests
{
    private static HoldOutResult Judge(double hold, int trades = 40, double dev = 0.20, bool counts = true) => HoldOutRule.Evaluate(dev, hold, trades, 30, 0.5, counts);

    [Theory]
    [InlineData(0.12, "pass")]
    [InlineData(0.10, "pass")]
    [InlineData(0.08, "fail")]
    [InlineData(0.0, "fail")]
    [InlineData(-0.05, "fail")]
    public void The_spec_table(double hold, string status)
    {
        var result = Judge(hold);

        result.Status.ShouldBe(status);
        result.Passed.ShouldBe(status == "pass");
    }

    [Fact]
    public void Sixty_percent_is_a_pass_with_the_ratio_shown()
    {
        Judge(0.12).Ratio.ShouldNotBeNull().ShouldBe(0.6, 1e-9);
    }

    [Fact]
    public void Twenty_two_trades_is_inconclusive_even_with_a_good_number()
    {
        var result = Judge(0.12, trades: 22);

        result.Status.ShouldBe("inconclusive");
        result.Passed.ShouldBeFalse();
    }

    [Fact]
    public void Exactly_thirty_trades_is_enough()
    {
        Judge(0.12, trades: 30).Status.ShouldBe("pass");
        Judge(0.12, trades: 29).Status.ShouldBe("inconclusive");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_hold_out_figure_that_is_not_finite_fails(double hold)
    {
        var result = Judge(hold);

        result.Status.ShouldBe("fail");
        result.Passed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void A_development_figure_that_is_not_above_zero_is_inconclusive_never_a_pass(double dev)
    {
        var result = Judge(0.5, dev: dev);

        result.Status.ShouldBe("inconclusive");
        result.Passed.ShouldBeFalse();
    }

    [Fact]
    public void A_re_used_window_shows_the_numbers_but_the_pass_flag_is_false_whatever_they_are()
    {
        var result = Judge(0.30, counts: false);

        result.Passed.ShouldBeFalse();
        result.Ratio.ShouldNotBeNull();
        result.Reason.ShouldContain("cannot count toward a pass");
        Judge(0.01, counts: false).Passed.ShouldBeFalse();
    }
}
