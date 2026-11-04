using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-05 criteria 2 and 3: the stress arithmetic by hand, the mean of the stresses equals the stress of the means, the gate on the research examples, and NaN or an unchecked sheet never passes.</summary>
public sealed class CostStressTests
{
    private static readonly CostStressOptions Options = new();

    [Theory]
    [InlineData(1.0, 0.87)]
    [InlineData(1.5, 0.805)]
    [InlineData(2.0, 0.74)]
    public void One_trade_of_the_spec_stresses_to_the_printed_values(double m, double expected)
    {
        // RExact +1.00, SlippageR -0.05 (RFilled 0.95), net 0.87: chargesR 0.08, so net R at m is 1.00 - 0.13 m
        CostStress.StressedR(1.00, 0.87, m).ShouldBe(expected, 1e-12);
    }

    [Fact]
    public void The_stress_equals_exact_plus_m_times_slippage_minus_charges()
    {
        const double rExact = 1.00, slippage = -0.05, filled = rExact + slippage, net = 0.87;
        var chargesR = filled - net;

        chargesR.ShouldBe(0.08, 1e-12);
        CostStress.StressedR(rExact, net, 1.5).ShouldBe(rExact + (1.5 * (slippage - chargesR)), 1e-12);
    }

    [Fact]
    public void The_mean_over_random_sets_equals_the_mean_of_the_per_trade_values_and_never_rises_with_m()
    {
        var rng = new Random(20261104);
        for (var set = 0; set < 200; set++)
        {
            var n = rng.Next(1, 60);
            var trades = new List<StressTrade>();
            for (var i = 0; i < n; i++)
            {
                var exact = (rng.NextDouble() * 4) - 2;
                var net = exact - (rng.NextDouble() * 0.3);   // slippage and charges only ever cost
                trades.Add(new StressTrade(exact, net));
            }

            double? previous = null;
            foreach (var m in new[] { 1.0, 1.5, 2.0, 3.0 })
            {
                var mean = CostStress.Mean(trades, m)!.Value;
                mean.ShouldBe(trades.Average(t => CostStress.StressedR(t.RExact, t.NetR, m)), 1e-9);
                if (previous is { } p)
                {
                    mean.ShouldBeLessThanOrEqualTo(p + 1e-12, "a dearer cost never raises the expectancy");
                }

                previous = mean;
            }
        }
    }

    [Fact]
    public void An_edge_of_plus_020_with_012_of_cost_and_slippage_passes_at_one_and_a_half_and_two_times_and_is_tradable()
    {
        // 1x net +0.20 with cost-plus-slippage 0.12 (RExact 0.32): 1.5x = 0.20 - 0.06 = +0.14, 2x = +0.08
        var result = CostStress.Evaluate([new StressTrade(0.32, 0.20)], Options, oneXThreshold: 0.10, oneXPassed: true, costsChecked: true);

        result.Points.Select(p => p.Multiplier).ShouldBe([1, 1.5, 2]);
        result.Points[1].ExpectancyR!.Value.ShouldBe(0.14, 1e-12);
        result.Points[1].Status.ShouldBe(StressStatus.Pass);
        result.Points[2].ExpectancyR!.Value.ShouldBe(0.08, 1e-12);
        result.Points[2].Status.ShouldBe(StressStatus.Pass);
        result.Tradable.ShouldBe(StressStatus.Pass);
    }

    [Fact]
    public void An_edge_of_plus_010_with_012_of_cost_fails_at_both_and_is_not_tradable()
    {
        // 1x net +0.10: 1.5x = 0.10 - 0.06 = +0.04 (needs 0.08), 2x = -0.02
        var result = CostStress.Evaluate([new StressTrade(0.22, 0.10)], Options, oneXThreshold: 0.05, oneXPassed: true, costsChecked: true);

        result.Points[1].ExpectancyR!.Value.ShouldBe(0.04, 1e-12);
        result.Points[1].Status.ShouldBe(StressStatus.Fail);
        result.Points[2].ExpectancyR!.Value.ShouldBe(-0.02, 1e-12);
        result.Points[2].Status.ShouldBe(StressStatus.Fail);
        result.Tradable.ShouldBe(StressStatus.Fail);
    }

    [Fact]
    public void The_floors_are_at_least_for_the_middle_and_greater_than_for_the_high_multiplier()
    {
        // binary-exact numbers: RExact 0.5, net 0.25 gives 0.125 at 1.5x (equal to the floor: passes) and exactly 0 at 2x (not greater than 0: fails)
        var result = CostStress.Evaluate([new StressTrade(0.5, 0.25)], new CostStressOptions(MidFloorR: 0.125), oneXThreshold: 0.1, oneXPassed: true, costsChecked: true);

        result.Points[1].ExpectancyR.ShouldBe(0.125);
        result.Points[1].Status.ShouldBe(StressStatus.Pass, "at least");
        result.Points[2].ExpectancyR.ShouldBe(0.0);
        result.Points[2].Status.ShouldBe(StressStatus.Fail, "greater than 0, not at least 0");
    }

    [Fact]
    public void The_one_times_row_follows_the_existing_gate_and_is_not_multiplied_by_the_trial_factor()
    {
        var failedGate = CostStress.Evaluate([new StressTrade(0.32, 0.20)], Options, oneXThreshold: 0.30, oneXPassed: false, costsChecked: true);

        failedGate.Points[0].Status.ShouldBe(StressStatus.Fail);
        failedGate.Points[0].Threshold.ShouldBe(0.30);
        failedGate.Points[1].Threshold.ShouldBe(0.08, "the floors are as configured, whatever the variants");
        failedGate.Tradable.ShouldBe(StressStatus.Fail, "the label uses the worst of the three");
    }

    [Fact]
    public void A_number_that_is_not_finite_or_no_trade_is_unavailable_and_never_a_pass()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity })
        {
            var result = CostStress.Evaluate([new StressTrade(0.3, bad)], Options, 0.1, true, true);

            result.Points.ShouldAllBe(p => p.Status == StressStatus.Unavailable && p.ExpectancyR == null);
            result.Tradable.ShouldBe(StressStatus.Unavailable);
        }

        var none = CostStress.Evaluate([], Options, 0.1, true, true);
        none.Points.ShouldAllBe(p => p.Status == StressStatus.Unavailable);
        none.Points[0].Reason.ShouldContain("no out-of-sample trade");
    }

    [Fact]
    public void An_unchecked_sheet_makes_every_row_unavailable_with_the_label_and_keeps_the_figures()
    {
        var result = CostStress.Evaluate([new StressTrade(0.32, 0.20)], Options, 0.1, true, costsChecked: false);

        result.Points.ShouldAllBe(p => p.Status == StressStatus.Unavailable && p.Reason.Contains("costs not checked against a bill", StringComparison.Ordinal));
        result.Points[1].ExpectancyR!.Value.ShouldBe(0.14, 1e-12);
        result.Tradable.ShouldBe(StressStatus.Unavailable);
    }

    [Fact]
    public void The_formula_and_the_first_order_limit_are_printed_texts()
    {
        CostStress.Formula.ShouldContain("RExact + m x");
        CostStress.FirstOrderLimit.ShouldContain("first-order for slippage");
    }
}
