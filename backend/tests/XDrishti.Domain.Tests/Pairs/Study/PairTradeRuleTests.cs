using XDrishti.Domain.PairResearch;

namespace XDrishti.Domain.Tests.Pairs.Study;

/// <summary>P14-13: sizing, the rupee risk and the R unit (the hand example of the spec) and the rule that a pair opens only when entry &lt;= |z| &lt; stop.</summary>
public sealed class PairTradeRuleTests
{
    [Fact]
    public void The_hand_example_gives_a_risk_of_600_rupees_and_075_R_for_a_450_rupee_gross_profit()
    {
        var risk = PairTradeRule.RupeeRisk(100_000m, 2.0, 0.004, 3.5);

        risk.ShouldBe(600.00m);
        PairTradeRule.R(450m, risk!.Value).ShouldBe(0.75, 1e-12);
    }

    [Fact]
    public void The_risk_shrinks_as_the_entry_z_nears_the_stop_and_is_unavailable_at_or_beyond_it()
    {
        PairTradeRule.RupeeRisk(100_000m, 3.0, 0.004, 3.5).ShouldBe(200.00m);
        PairTradeRule.RupeeRisk(100_000m, -3.0, 0.004, 3.5).ShouldBe(200.00m);
        PairTradeRule.RupeeRisk(100_000m, 3.5, 0.004, 3.5).ShouldBeNull();
        PairTradeRule.RupeeRisk(100_000m, 3.9, 0.004, 3.5).ShouldBeNull();
    }

    [Theory]
    [InlineData(double.NaN, 0.004)]
    [InlineData(2.0, double.NaN)]
    [InlineData(2.0, 0.0)]
    [InlineData(2.0, -0.1)]
    [InlineData(double.PositiveInfinity, 0.004)]
    public void A_risk_that_cannot_be_computed_is_unavailable_never_a_number(double z, double sd)
    {
        PairTradeRule.RupeeRisk(100_000m, z, sd, 3.5).ShouldBeNull();
    }

    [Fact]
    public void R_of_a_non_positive_risk_is_not_a_number()
    {
        double.IsNaN(PairTradeRule.R(100m, 0m)).ShouldBeTrue();
    }

    [Fact]
    public void Whole_shares_are_floored_and_the_x_leg_is_beta_times_the_notional()
    {
        var size = PairTradeRule.Size(100_000m, 100.90, 100.0, 1.0);

        size.ShouldBe(new PairSizing(991, 1000));
        PairTradeRule.Size(100_000m, 250.0, 80.0, 0.5).ShouldBe(new PairSizing(400, 625));
    }

    [Theory]
    [InlineData(100_000, 200_000.0, 100.0, 1.0)]
    [InlineData(100_000, 100.0, 200_000.0, 1.0)]
    [InlineData(100_000, 100.0, 100.0, 0.0)]
    [InlineData(100_000, 100.0, 100.0, -0.4)]
    [InlineData(100_000, 0.0, 100.0, 1.0)]
    [InlineData(100_000, 100.0, double.NaN, 1.0)]
    [InlineData(100_000, 100.0, 100.0, double.NaN)]
    [InlineData(0, 100.0, 100.0, 1.0)]
    public void A_leg_of_zero_shares_or_a_bad_input_is_not_sizable(int notional, double y, double x, double beta)
    {
        PairTradeRule.Size(notional, y, x, beta).ShouldBeNull();
    }

    [Theory]
    [InlineData(1.99, false)]
    [InlineData(2.0, true)]
    [InlineData(-2.0, true)]
    [InlineData(3.49, true)]
    [InlineData(-3.49, true)]
    [InlineData(3.5, false)]
    [InlineData(-3.5, false)]
    [InlineData(9.0, false)]
    [InlineData(double.NaN, false)]
    public void A_pair_opens_only_when_the_entry_level_is_reached_and_the_stop_is_not(double z, bool opens)
    {
        PairTradeRule.MayOpen(z, 2.0, 3.5).ShouldBe(opens);
    }
}
