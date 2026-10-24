using XDrishti.Domain.Reporting;
using static XDrishti.Domain.Tests.Reporting.ReportFixtures;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>The auto-insights: rule-based sentences whose numbers come from the cells, and honest reason states below the 15-trade minimum (P8-05, ADR 0099).</summary>
public sealed class InsightTests
{
    private static readonly ReportOptions Defaults = new();

    private static Insight Of(IReadOnlyList<Insight> insights, InsightKind kind) => insights.Single(i => i.Kind == kind);

    [Fact]
    public void There_are_always_eight_insights_in_the_same_order()
    {
        var insights = Population([Run("r1")], [], Defaults).Insights();

        insights.Select(i => i.Kind).ShouldBe(
        [
            InsightKind.BestStrategy, InsightKind.WeakestStrategy, InsightKind.BasketGap, InsightKind.SideBias,
            InsightKind.BestExit, InsightKind.Weekday, InsightKind.Regime, InsightKind.Calibration,
        ]);
    }

    [Fact]
    public void Best_and_weakest_strategy_are_named_with_their_figures_when_both_have_fifteen_trades()
    {
        // A: 12 wins 8 losses (+0.20 R, 60 %); B: 8 wins 12 losses (-0.20 R, 40 %); C: 10 trades, below the minimum
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(12, 8)), .. Series("r1", "B", WinsAndLosses(8, 12)), .. Series("r1", "C", WinsAndLosses(10, 0))];

        var insights = Population([Run("r1")], trades, Defaults).Insights();

        var best = Of(insights, InsightKind.BestStrategy);
        best.State.ShouldBe(MetricState.Value);
        best.Text.ShouldBe("Best strategy: A +0.20 R per trade over 20 trades (win rate 60 %, in-sample, thin sample).");
        best.Cells.ShouldBe(["Strategy=A"]);
        var weakest = Of(insights, InsightKind.WeakestStrategy);
        weakest.Text.ShouldBe("Weakest strategy: B -0.20 R per trade over 20 trades (win rate 40 %, in-sample, thin sample).");
        weakest.Cells.ShouldBe(["Strategy=B"]);
    }

    [Fact]
    public void With_no_group_at_fifteen_trades_the_insight_says_so_and_states_no_figure()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(6, 4)), .. Series("r1", "B", WinsAndLosses(4, 6))];

        var best = Of(Population([Run("r1")], trades, Defaults).Insights(), InsightKind.BestStrategy);

        best.State.ShouldBe(MetricState.Insufficient);
        best.Text.ShouldBe("Best strategy: no group has 15 trades yet, so no strategy is compared.");
        best.Cells.ShouldBeEmpty();
        best.Text.ShouldNotContain("R per trade");
    }

    [Fact]
    public void With_one_group_at_fifteen_trades_there_is_nothing_to_compare_it_with()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(12, 8)), .. Series("r1", "B", WinsAndLosses(4, 6))];

        var weakest = Of(Population([Run("r1")], trades, Defaults).Insights(), InsightKind.WeakestStrategy);

        weakest.State.ShouldBe(MetricState.Insufficient);
        weakest.Text.ShouldBe("Weakest strategy: only A has 15 trades; there is nothing to compare it with.");
    }

    [Fact]
    public void With_one_strategy_only_nothing_is_compared()
    {
        var best = Of(Population([Run("r1")], Series("r1", "A", WinsAndLosses(12, 8)), Defaults).Insights(), InsightKind.BestStrategy);

        best.State.ShouldBe(MetricState.NotEvaluable);
        best.Text.ShouldBe("Best strategy: not evaluable, only one strategy (A) is in the runs, so none is compared.");
    }

    [Fact]
    public void Strategies_with_the_same_expectancy_are_reported_as_no_difference_not_as_a_winner()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(10, 10)), .. Series("r1", "B", WinsAndLosses(10, 10))];

        var insights = Population([Run("r1")], trades, Defaults).Insights();

        Of(insights, InsightKind.BestStrategy).Text.ShouldBe("No strategy stands out: the 2 compared each have 0.00 R per trade.");
        Of(insights, InsightKind.WeakestStrategy).Text.ShouldBe("No strategy stands out: the 2 compared each have 0.00 R per trade.");
    }

    [Fact]
    public void The_basket_gap_names_the_best_and_worst_basket_and_says_they_overlap()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(12, 8), baskets: ["North"]), .. Series("r1", "B", WinsAndLosses(8, 12), baskets: ["South"])];

        var gap = Of(Population([Run("r1")], trades, Defaults).Insights(), InsightKind.BasketGap);

        gap.State.ShouldBe(MetricState.Value);
        gap.Text.ShouldBe("Basket gap: North +0.20 R per trade over 20 trades (win rate 60 %, in-sample, thin sample) against South -0.20 R per trade over 20 trades (win rate 40 %, in-sample, thin sample), a gap of 0.40 R. Baskets overlap, so these figures are not additive.");
    }

    [Fact]
    public void Without_basket_membership_the_basket_gap_is_unavailable()
    {
        var gap = Of(Population([Run("r1")], Series("r1", "A", WinsAndLosses(12, 8)), Defaults).Insights(), InsightKind.BasketGap);

        gap.State.ShouldBe(MetricState.Unavailable);
        gap.Text.ShouldBe("Basket gap: unavailable, no trade records the baskets of its instrument.");
    }

    [Fact]
    public void Side_bias_compares_the_two_sides()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(12, 8), side: "BUY"), .. Series("r1", "A", WinsAndLosses(8, 12), side: "SELL")];

        var side = Of(Population([Run("r1")], trades, Defaults).Insights(), InsightKind.SideBias);

        side.Text.ShouldBe("Side bias: BUY +0.20 R per trade over 20 trades (win rate 60 %, in-sample, thin sample) against SELL -0.20 R per trade over 20 trades (win rate 40 %, in-sample, thin sample), a gap of 0.40 R.");
    }

    [Fact]
    public void The_best_exit_needs_two_exits_and_an_exit_that_was_recorded()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(12, 8)), .. Series("r2", "A", WinsAndLosses(8, 12))];
        var two = Population([Run("r1", exit: "FIXED_RR"), Run("r2", exit: "TRAIL")], trades, Defaults);
        var none = Population([Run("r1", exit: null)], Series("r1", "A", WinsAndLosses(12, 8)), Defaults);
        var one = Population([Run("r1")], Series("r1", "A", WinsAndLosses(12, 8)), Defaults);

        Of(two.Insights(), InsightKind.BestExit).Text.ShouldBe("Best exit: FIXED_RR +0.20 R per trade over 20 trades (win rate 60 %, in-sample, thin sample).");
        Of(none.Insights(), InsightKind.BestExit).State.ShouldBe(MetricState.Unavailable);
        Of(one.Insights(), InsightKind.BestExit).State.ShouldBe(MetricState.NotEvaluable);
    }

    [Fact]
    public void When_the_runs_hold_the_same_signals_under_two_exits_only_the_exit_insight_is_evaluable()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(12, 8)), .. Series("r2", "A", WinsAndLosses(8, 12))];
        var insights = Population([Run("r1", exit: "FIXED_RR"), Run("r2", exit: "TRAIL")], trades, Defaults).Insights();

        Of(insights, InsightKind.BestStrategy).State.ShouldBe(MetricState.NotEvaluable);
        Of(insights, InsightKind.BestStrategy).Text.ShouldContain("count a signal once per label");
        Of(insights, InsightKind.SideBias).State.ShouldBe(MetricState.NotEvaluable);
        Of(insights, InsightKind.Weekday).State.ShouldBe(MetricState.NotEvaluable);
        Of(insights, InsightKind.BestExit).State.ShouldBe(MetricState.Value);
    }

    [Fact]
    public void Regime_is_unavailable_and_calibration_not_evaluable()
    {
        var insights = Population([Run("r1")], Series("r1", "A", WinsAndLosses(12, 8)), Defaults).Insights();

        Of(insights, InsightKind.Regime).State.ShouldBe(MetricState.Unavailable);
        Of(insights, InsightKind.Calibration).State.ShouldBe(MetricState.NotEvaluable);
        Of(insights, InsightKind.Regime).Cells.ShouldBeEmpty();
    }

    [Fact]
    public void A_state_other_than_value_never_states_a_figure_of_a_cell()
    {
        var insights = Population([Run("r1")], Series("r1", "A", WinsAndLosses(6, 4)), Defaults).Insights();

        insights.Where(i => i.State != MetricState.Value).ShouldAllBe(i => !i.Text.Contains(" R per trade", StringComparison.Ordinal) && i.Cells.Count == 0);
    }

    [Fact]
    public void The_minimum_for_an_insight_is_the_option_not_a_constant()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(3, 2)), .. Series("r1", "B", WinsAndLosses(2, 3))];

        var best = Of(Population([Run("r1")], trades, new ReportOptions { InsightMinTrades = 5 }).Insights(), InsightKind.BestStrategy);

        best.State.ShouldBe(MetricState.Value);
        best.Text.ShouldBe("Best strategy: A +0.20 R per trade over 5 trades (win rate 60 %, in-sample, thin sample).");
    }

    [Fact]
    public void A_cell_with_the_cell_minimum_of_trades_carries_no_thin_sample_note()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(24, 16)), .. Series("r1", "B", WinsAndLosses(16, 24))];

        var best = Of(Population([Run("r1")], trades, Defaults).Insights(), InsightKind.BestStrategy);

        best.Text.ShouldBe("Best strategy: A +0.20 R per trade over 40 trades (win rate 60 %).");
    }

    [Fact]
    public void When_only_the_style_varies_the_exit_insight_names_the_style_too()
    {
        List<ReportTrade> trades = [.. Series("r1", "A", WinsAndLosses(12, 8)), .. Series("r2", "A", WinsAndLosses(8, 12))];

        var insights = Population([Run("r1", style: "intraday"), Run("r2", style: "swing")], trades, Defaults).Insights();

        Of(insights, InsightKind.BestExit).Text.ShouldStartWith("Best exit and style: FIXED_RR / intraday +0.20 R");
    }
}
