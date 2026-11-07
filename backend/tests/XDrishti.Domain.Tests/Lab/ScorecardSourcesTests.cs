using XDrishti.Domain.Lab;
using XDrishti.Domain.Readiness;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>SC-S1 (ADR 0222): the figures the Lab computes for rows 10 and 11 of the readiness scorecard, with hand tables.</summary>
public sealed class ScorecardSourcesTests
{
    private static SelectionTrade T(int y, int m, double r) => new("c", new DateOnly(y, m, 5), new DateOnly(y, m, 6), r);

    private static double? Share(params SelectionTrade[] trades) =>
        QuarterBreakdown.Share([.. QuarterBreakdown.Of(trades).Select(q => q.MeanR)], EdgeChecklist.QuarterFloorR);

    // ---- row 11: the 90th percentile of the Monte Carlo drawdown ----

    [Fact]
    public void The_90th_percentile_of_a_sorted_0_to_10_is_the_value_at_rank_9()
    {
        double[] sorted = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        MonteCarlo.Percentile(sorted, 0.90).ShouldBe(9);
    }

    [Fact]
    public void Monte_carlo_gives_the_90th_percentile_of_the_drawdown_and_leaves_the_50th_and_95th_as_they_were()
    {
        // seed 0, 4 draws, trades [1,-3]: drawdowns sorted 0 3 3 3, ranks round(p x 3) = 2 (0.50), 3 (0.90), 3 (0.95)
        var mc = MonteCarlo.Run([1, -3], 4, 0)!;

        (mc.DrawdownP50, mc.DrawdownP90, mc.DrawdownP95).ShouldBe((3.0, 3.0, 3.0));
        // trades [3,-1], drawdowns sorted 0 1 1 1
        var other = MonteCarlo.Run([3, -1], 4, 0)!;
        (other.DrawdownP50, other.DrawdownP90, other.DrawdownP95).ShouldBe((1.0, 1.0, 1.0));
    }

    [Fact]
    public void Monte_carlo_of_50_trades_at_seed_42_keeps_the_figures_computed_on_main_before_the_90th_was_added()
    {
        // golden: computed with the Run loop and SeededRandom of main (before SC-S1) on the same input; an edit that consumes one more draw, or reads the generator in another order, moves every figure
        var results = Enumerable.Range(0, 50).Select(i => (((i * 7) % 13) - 5) * 0.25).ToList();

        var mc = MonteCarlo.Run(results, 1000, 42)!;

        mc.ObservedMeanR.ShouldBe(0.22, 1e-9);
        mc.MeanP05.ShouldBe(-0.01, 1e-9);
        mc.MeanP50.ShouldBe(0.22, 1e-9);
        mc.MeanP95.ShouldBe(0.435, 1e-9);
        mc.DrawdownP50.ShouldBe(3.75, 1e-9);
        mc.DrawdownP95.ShouldBe(8, 1e-9);
        mc.ShareNotPositive.ShouldBe(0.059, 1e-9);
        mc.DrawdownP90!.Value.ShouldBeInRange(3.75, 8);
    }

    [Fact]
    public void The_90th_percentile_sits_between_the_50th_and_the_95th_and_the_same_seed_gives_the_same_result()
    {
        double[] results = [2, -1, 2, -1, -1, 2, -1, 2, 2, -1];

        var mc = MonteCarlo.Run(results, 1000, 5)!;

        mc.DrawdownP90.ShouldNotBeNull();
        mc.DrawdownP90.Value.ShouldBeInRange(mc.DrawdownP50, mc.DrawdownP95);
        MonteCarlo.Run(results, 1000, 5).ShouldBe(mc);
    }

    // ---- row 10: the quarters ----

    [Fact]
    public void Quarter_means_are_grouped_by_calendar_quarter_of_the_entry_and_a_quarter_without_a_trade_is_not_a_row()
    {
        var rows = QuarterBreakdown.Of([T(2025, 2, 0.1), T(2025, 3, 0.3), T(2025, 12, -1), T(2026, 1, 2)]);

        rows.ShouldBe([new QuarterRow(2025, 1, 2, 0.2), new QuarterRow(2025, 4, 1, -1), new QuarterRow(2026, 1, 1, 2)]);
    }

    [Fact]
    public void Four_quarters_with_means_plus_0_10_minus_0_04_minus_0_06_plus_0_20_give_75_percent()
    {
        var share = Share(T(2025, 1, 0.10), T(2025, 4, -0.04), T(2025, 7, -0.06), T(2025, 10, 0.20));

        share.ShouldBe(0.75);
        (share >= ReadinessOptions.DefaultMinQuarterShare).ShouldBeTrue();
    }

    [Fact]
    public void A_quarter_mean_of_exactly_minus_0_05_passes_the_floor()
    {
        // two trades -0.04 and -0.06 average to -0.05 up to the last bit of the sum
        Share(T(2025, 1, -0.04), T(2025, 2, -0.06)).ShouldBe(1.0);
    }

    [Fact]
    public void A_quarter_mean_just_under_the_floor_fails_it()
    {
        Share(T(2025, 1, -0.0501)).ShouldBe(0.0);
    }

    [Fact]
    public void Two_of_three_quarters_give_66_7_percent_which_is_under_the_70_percent_proposal()
    {
        var share = Share(T(2025, 1, 0.1), T(2025, 4, 0.2), T(2025, 7, -0.5));

        share!.Value.ShouldBe(2.0 / 3, 1e-12);
        (share >= ReadinessOptions.DefaultMinQuarterShare).ShouldBeFalse();
    }

    [Fact]
    public void No_trade_gives_no_quarter_share()
    {
        Share().ShouldBeNull();
    }

    [Fact]
    public void The_floor_of_the_quarter_share_is_the_one_of_the_checklist_and_of_the_decay_monitor()
    {
        EdgeChecklist.QuarterFloorR.ShouldBe(-0.05);
        XDrishti.Domain.Confidence.DecayOptions.DefaultQuarterFloorR.ShouldBe(EdgeChecklist.QuarterFloorR);
    }

    // ---- row 10: the instruments ----

    private static double? Max(params (string Instrument, double R)[] trades) => InstrumentShare.Max(trades);

    [Fact]
    public void Instrument_totals_3_1_and_minus_1_give_the_share_1()
    {
        // total 3; the largest instrument holds 3 of it
        Max(("a", 3), ("b", 1), ("c", -1)).ShouldBe(1.0);
    }

    [Fact]
    public void An_instrument_that_made_more_than_the_total_because_the_others_lost_is_capped_at_1()
    {
        // a made 4 R, b lost 1 R: total 3, 4 / 3 would be 1.33
        Max(("a", 4), ("b", -1)).ShouldBe(1.0);
    }

    [Fact]
    public void Instrument_totals_of_2_1_1_give_the_share_a_half()
    {
        Max(("a", 1), ("a", 1), ("b", 1), ("c", 1)).ShouldBe(0.5);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    public void A_total_not_above_zero_gives_the_share_1(double second)
    {
        Max(("a", 2), ("b", second - 2)).ShouldBe(1.0);
    }

    [Fact]
    public void No_trade_gives_no_instrument_share()
    {
        Max().ShouldBeNull();
    }
}
