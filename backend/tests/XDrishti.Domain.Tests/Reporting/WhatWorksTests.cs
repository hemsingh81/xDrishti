using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;
using static XDrishti.Domain.Tests.Reporting.ReportFixtures;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>"What works": ranked by the credible lower bound of expectancy, thin cells listed after, and "nothing has enough evidence yet" as an answer (P8-05, ADR 0099).</summary>
public sealed class WhatWorksTests
{
    private static readonly ReportDimension[] ByStrategy = [ReportDimension.Strategy];

    private static ReportOptions Options() => new() { Stats = new CellStatsOptions(PriorStrength: 20, MinTrades: 5) };

    private static List<ReportTrade> Cells(params (string Strategy, int Wins, int Losses)[] cells)
    {
        List<ReportTrade> trades = [];
        foreach (var (strategy, wins, losses) in cells)
        {
            trades.AddRange(Series("r1", strategy, WinsAndLosses(wins, losses)));
        }

        return trades;
    }

    [Fact]
    public void Cells_with_enough_sample_are_ranked_by_the_lower_bound_and_the_rest_follow_with_the_reason()
    {
        var population = Population([Run("r1")], Cells(("S1", 8, 2), ("S2", 5, 5), ("S3", 3, 1), ("S4", 0, 10)), Options());

        var answer = population.FindWhatWorks(ByStrategy).Value;

        answer.Kind.ShouldBe(WhatWorksKind.Ranked);
        answer.Ranked.Select(r => (r.Rank, r.Group.Key.Text)).ShouldBe([(1, "Strategy=S1"), (2, "Strategy=S2")]);
        answer.Ranked[0].Group.ExpectancyLowerR.Number!.Value.ShouldBeGreaterThan(answer.Ranked[1].Group.ExpectancyLowerR.Number!.Value);
        answer.Ranked.ShouldAllBe(r => r.LowerBoundPositive == (r.Group.ExpectancyLowerR.Number!.Value > 0));
        answer.NotRanked.Select(u => u.Group.Key.Text).ShouldBe(["Strategy=S4", "Strategy=S3"]);
        answer.NotRanked[0].Reason.ShouldBe("no lower bound can be formed: no winning trade");
        answer.NotRanked[1].Reason.ShouldBe("insufficient sample: 4 trades, at least 5 needed");
        answer.CellsLookedAt.ShouldBe(4);
        answer.LargestN.ShouldBe(10);
        answer.MinTrades.ShouldBe(5);
        answer.Headline.ShouldStartWith("2 of 4 cells have 5 or more trades and are ranked by the credible lower bound of expectancy;");
        answer.SelectionNote.ShouldContain("the best of 4 will look good by chance");
        answer.Evidence.Population.N.ShouldBe(34);
    }

    [Fact]
    public void The_lower_bound_ranks_a_big_modest_cell_above_a_thin_lucky_one_although_the_raw_mean_says_the_opposite()
    {
        // 4 wins and 1 loss (mean +0.60 R) against 60 wins and 40 losses (mean +0.20 R)
        var population = Population([Run("r1")], Cells(("THIN", 4, 1), ("BIG", 60, 40), ("OTHER", 0, 6)), Options());

        var answer = population.FindWhatWorks(ByStrategy).Value;

        answer.Ranked.Select(r => r.Group.Key.Text).ShouldBe(["Strategy=BIG", "Strategy=THIN"]);
        var thin = answer.Ranked[1].Group;
        var big = answer.Ranked[0].Group;
        thin.ExpectancyR.Number!.Value.ShouldBeGreaterThan(big.ExpectancyR.Number!.Value);
        big.ExpectancyLowerR.Number!.Value.ShouldBeGreaterThan(thin.ExpectancyLowerR.Number!.Value);
        answer.NotRanked.Select(u => u.Group.Key.Text).ShouldBe(["Strategy=OTHER"]);
    }

    [Fact]
    public void When_no_cell_has_the_minimum_the_answer_is_nothing_has_enough_evidence_yet_with_the_counts()
    {
        var population = Population([Run("r1")], Cells(("S1", 3, 1), ("S2", 2, 2), ("S3", 1, 0)), Options());

        var answer = population.FindWhatWorks(ByStrategy).Value;

        answer.Kind.ShouldBe(WhatWorksKind.NothingQualifies);
        answer.Ranked.ShouldBeEmpty();
        answer.NotRanked.Count.ShouldBe(3);
        answer.NotRanked.ShouldAllBe(u => u.Reason.StartsWith("insufficient sample", StringComparison.Ordinal));
        answer.CellsLookedAt.ShouldBe(3);
        answer.LargestN.ShouldBe(4);
        answer.Headline.ShouldBe("Nothing has enough evidence yet: looked at 3 cells, the largest has 4 trades and 5 are needed.");
        answer.NotRanked.ShouldAllBe(u => u.Group.ExpectancyLowerR.Number == null && u.Group.ExpectancyR.Number == null);
    }

    [Fact]
    public void With_no_trade_at_all_there_is_nothing_to_rank_and_it_says_so()
    {
        var answer = Population([Run("r1")], [], Options()).FindWhatWorks(ByStrategy).Value;

        answer.Kind.ShouldBe(WhatWorksKind.NothingQualifies);
        answer.CellsLookedAt.ShouldBe(0);
        answer.Headline.ShouldBe("Nothing to rank: the chosen runs hold no trades.");
    }

    [Fact]
    public void Cells_with_the_minimum_trades_but_no_lower_bound_are_not_ranked_and_the_headline_says_why()
    {
        var population = Population([Run("r1")], Cells(("S1", 0, 10), ("S2", 10, 0)), Options());

        var answer = population.FindWhatWorks(ByStrategy).Value;

        answer.Kind.ShouldBe(WhatWorksKind.NothingQualifies);
        answer.Headline.ShouldContain("2 with 5 or more trades have no lower bound");
    }

    [Fact]
    public void Ties_are_broken_by_more_trades_then_by_the_key_whatever_the_input_order()
    {
        List<ReportTrade> trades = [.. Cells(("S5", 6, 4), ("S6", 6, 4), ("S7", 12, 8))];
        var answers = new List<WhatWorks>();
        var random = new Random(7);
        for (var i = 0; i < 10; i++)
        {
            var shuffled = trades.OrderBy(_ => random.Next()).ToList();
            answers.Add(Population([Run("r1")], shuffled, Options()).FindWhatWorks(ByStrategy).Value);
        }

        answers.ShouldAllBe(a => a == answers[0]);
        var order = answers[0].Ranked.Select(r => r.Group.Key.Text).ToList();
        order.IndexOf("Strategy=S5").ShouldBeLessThan(order.IndexOf("Strategy=S6"));
    }

    [Fact]
    public void The_default_cell_is_basket_strategy_timeframe_side_and_exit_and_leaves_out_what_nobody_recorded()
    {
        var withBaskets = Population([Run("r1")], Series("r1", "ORB", WinsAndLosses(6, 4), baskets: ["A"]), Options());
        var without = Population([Run("r1", exit: null)], Series("r1", "ORB", WinsAndLosses(6, 4)), Options());

        var a = withBaskets.FindWhatWorks().Value;
        var b = without.FindWhatWorks().Value;

        a.Dimensions.ShouldBe([ReportDimension.Basket, ReportDimension.Strategy, ReportDimension.Timeframe, ReportDimension.Side, ReportDimension.Exit]);
        a.UnavailableDimensions.ShouldBeEmpty();
        a.NonAdditive.ShouldBeFalse();
        b.Dimensions.ShouldBe([ReportDimension.Strategy, ReportDimension.Timeframe, ReportDimension.Side]);
        b.UnavailableDimensions.ShouldBe(["basket left out: no trade records the baskets of its instrument", "exit left out: no run records its exit policy"]);
    }

    [Fact]
    public void An_explicit_cell_with_an_exit_nobody_recorded_lists_the_cell_as_unavailable_not_ranked()
    {
        var population = Population([Run("r1", exit: null)], Series("r1", "ORB", WinsAndLosses(6, 4)), Options());

        var answer = population.FindWhatWorks([ReportDimension.Strategy, ReportDimension.Exit]).Value;

        answer.Kind.ShouldBe(WhatWorksKind.NothingQualifies);
        answer.NotRanked.Single().Reason.ShouldBe("exit not recorded for run r1");
    }

    [Fact]
    public void A_cell_whose_dimension_has_no_data_is_refused_not_ranked()
    {
        var population = Population([Run("r1")], Series("r1", "ORB", WinsAndLosses(6, 4)), Options());

        population.FindWhatWorks([ReportDimension.Regime]).Error.Code.ShouldBe("reports.dimension_unavailable");
    }

    [Fact]
    public void Overlapping_baskets_are_flagged_non_additive_in_the_answer()
    {
        var population = Population([Run("r1")], Series("r1", "ORB", WinsAndLosses(6, 4), baskets: ["A", "B"]), Options());

        var answer = population.FindWhatWorks([ReportDimension.Basket]).Value;

        answer.NonAdditive.ShouldBeTrue();
        answer.Ranked.Count.ShouldBe(2);
        answer.Ranked.Select(r => r.Group.N).ShouldBe([10, 10]);
    }
}
