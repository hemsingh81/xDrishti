using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>CF-11 part 1, AC2 and AC3 (ADR 0221): the anchors of the six sub-scores, piecewise linear and clamped, and the power by hand.</summary>
public sealed class ScoreAnchorsTests
{
    [Theory]
    [InlineData(15, 0)]
    [InlineData(22.5, 25)]
    [InlineData(30, 50)]
    [InlineData(45, 60.7142857)]
    [InlineData(100, 100)]
    [InlineData(0, 0)]
    [InlineData(250, 100)]
    public void Evidence_follows_its_anchors(double nEff, double expected) => ScoreAnchors.Evidence(nEff).ShouldBe(expected, 1e-6);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.05, 25)]
    [InlineData(0.10, 50)]
    [InlineData(0.125, 75)]
    [InlineData(0.15, 100)]
    [InlineData(-1, 0)]
    [InlineData(0.4, 100)]
    public void Effect_follows_its_anchors(double bound, double expected) => ScoreAnchors.Effect(bound).ShouldBe(expected, 1e-9);

    [Theory]
    [InlineData(0.30, 0)]
    [InlineData(0.3808, 20.2)]
    [InlineData(0.50, 50)]
    [InlineData(0.80, 100)]
    [InlineData(0.10, 0)]
    [InlineData(0.99, 100)]
    public void Power_follows_its_anchors(double power, double expected) => ScoreAnchors.PowerScore(power).ShouldBe(expected, 1e-6);

    [Theory]
    [InlineData(10, 0)]
    [InlineData(6, 40)]
    [InlineData(5, 50)]
    [InlineData(4, 75)]
    [InlineData(3, 100)]
    [InlineData(1, 100)]
    [InlineData(15, 0)]
    public void Calibration_follows_its_anchors(double ecePp, double expected) => ScoreAnchors.Calibration(ecePp).ShouldBe(expected, 1e-9);

    [Fact]
    public void The_power_of_45_effective_trades_at_0_3_R_is_38_08_percent()
    {
        ScoreAnchors.Power(45).ShouldBe(0.3808, 0.0005);
    }

    [Theory]
    [InlineData(60, 0.2, 0.270)]
    [InlineData(60, 0.5, 0.825)]
    public void The_power_agrees_with_the_p10_00_d7_check(double n, double delta, double expected)
    {
        ScoreAnchors.Power(n, delta).ShouldBe(expected, 0.001);
    }

    [Fact]
    public void The_effective_n_for_80_percent_power_inverts_the_power()
    {
        var n = ScoreAnchors.NEffForFullPower();

        n.ShouldBe(154.6, 0.2);
        ScoreAnchors.Power(n).ShouldBe(0.80, 1e-3);
    }

    [Fact]
    public void Every_anchor_function_is_clamped_and_never_decreases_with_better_input()
    {
        double previous = -1;
        for (var n = -10.0; n <= 400; n += 2.5)
        {
            var value = ScoreAnchors.Evidence(n);
            value.ShouldBeInRange(0, 100);
            value.ShouldBeGreaterThanOrEqualTo(previous);
            previous = value;
        }

        previous = -1;
        for (var n = 0.0; n <= 400; n += 2.5)
        {
            var value = ScoreAnchors.PowerScore(ScoreAnchors.Power(n));
            value.ShouldBeInRange(0, 100);
            value.ShouldBeGreaterThanOrEqualTo(previous);
            previous = value;
        }

        previous = 101;
        for (var ece = 0.0; ece <= 20; ece += 0.5)
        {
            var value = ScoreAnchors.Calibration(ece);
            value.ShouldBeInRange(0, 100);
            value.ShouldBeLessThanOrEqualTo(previous);
            previous = value;
        }
    }

    [Fact]
    public void The_lower_bound_is_the_mean_less_1_645_standard_errors()
    {
        ScoreAnchors.LowerBound(0.3, 1.0, 100).ShouldBe(0.3 - (1.645 * 0.1), 1e-12);
    }
}
