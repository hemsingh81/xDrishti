using System.Globalization;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Annex C of P3-08 (criteria C1 and C3): the oracle's costing and the engine's agree on every compared field (the fate, the quantity, the risk taken, both turnovers, the sixteen lines, the total, <c>CostR</c> and <c>RNet</c>) on the
/// author's generated cases and on the chain from the walks, and every category and sub-case of the spec reaches its floor of 500, measured from the oracle's own trace.
/// </summary>
public sealed class CostComparisonTests(ITestOutputHelper output)
{
    private const int Floor = 500;

    private static void Print(CostTally tally, ITestOutputHelper output)
    {
        foreach (var line in tally.Summary().Split('\n').Take(2))
        {
            output.WriteLine(line);
        }
    }

    [Fact]
    public void The_author_s_generated_cases_agree_with_the_engine_on_every_field()
    {
        var tally = CostCorpus.SeriesRun;

        Print(tally, output);
        tally.DisagreementCount.ShouldBe(0, string.Join("\n", tally.Disagreements.Take(10)));
        tally.Cases.ShouldBe(Enum.GetValues<OracleCostCategory>().Sum(CostCorpus.SeedsOf));
        tally.Kept.ShouldBeGreaterThan(Floor);
        tally.SkippedQuantity.ShouldBeGreaterThan(Floor);
        tally.SkippedCost.ShouldBeGreaterThan(Floor);
        tally.Errors.ShouldBe(0);
    }

    [Fact]
    public void The_chain_from_the_walks_agrees_with_the_engine_s_costed_trades_on_every_field()
    {
        var tally = CostCorpus.ChainRun;

        Print(tally, output);
        tally.DisagreementCount.ShouldBe(0, string.Join("\n", tally.Disagreements.Take(10)));
        tally.Cases.ShouldBeGreaterThan(2_000, "at least 2,000 trades on the TEST sheet and the fixture sheet");
        tally.Errors.ShouldBe(0);
    }

    [Fact]
    public void Every_category_is_reached_at_least_500_times_in_the_author_s_run_by_the_oracle_s_trace()
    {
        var tally = CostCorpus.SeriesRun;

        foreach (var category in Enum.GetValues<OracleCostCategory>())
        {
            var id = OracleCostCategories.TraceId(category);
            output.WriteLine($"{category} ({id}): {tally.Row(id)}");
            tally.Row(id).ShouldBeGreaterThanOrEqualTo(Floor, category.ToString());
        }
    }

    [Fact]
    public void Every_sub_case_of_the_spec_reaches_its_floor_in_the_author_s_run_and_in_the_chain_where_the_chain_can_reach_it()
    {
        // the spec's list: each rounding mode, a swing trade across a schedule change, quantity below 1, cost over the limit, equal to the limit, a delivery sell with the DP charge, an intraday trade, a sell
        string[] named =
        [
            "C-BROK-MIN", "C-ROUND-AWAY", "C-ROUND-EVEN", "C-ROUND-DOWN", CostTally.SwingAcrossScheduleChange, "C-QTY-ZERO", "C-COST-SKIP", CostTally.EqualToLimit, CostTally.DpOnDeliverySell, "C-SEG-INTRADAY", "C-TRADE-SELL", "C-TRADE-BUY", "C-SEG-DELIVERY",
        ];
        foreach (var id in named)
        {
            output.WriteLine($"{id}: author {CostCorpus.SeriesRun.Row(id)}, chain {CostCorpus.ChainRun.Row(id)}");
            CostCorpus.SeriesRun.Row(id).ShouldBeGreaterThanOrEqualTo(Floor, "the author's generator alone: " + id);
            if (id != "C-BROK-MIN")
            {
                CostCorpus.ChainRun.Row(id).ShouldBeGreaterThanOrEqualTo(Floor, "the chain alone: " + id);
            }
        }
    }

    [Fact]
    public void The_chain_uses_capitals_from_10_000_to_1_00_00_000_both_profiles_and_the_three_rounding_modes_of_the_TEST_sheet()
    {
        CostCorpus.ChainRun.MinCapital.ShouldBe(10_000m);
        CostCorpus.ChainRun.MaxCapital.ShouldBe(10_000_000m);
        CostCorpus.ChainSheets.Select(s => s.Schedules[0].Rounding).Distinct().Count().ShouldBe(3);
        CostCorpus.ChainSheets.Count.ShouldBe(7);
        CostCorpus.ChainSheets[^1].Id.ShouldBe(FixtureSheet.Id);
    }

    [Fact]
    public void The_summaries_of_the_two_cost_runs_are_printed_and_kept_when_a_report_folder_is_named()
    {
        var lines = CostCorpus.All.SelectMany(t => t.Summary().Split('\n')).ToList();

        Report.Write("cost-summary.txt", lines);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        lines.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_largest_ratio_difference_seen_is_printed_and_no_tolerance_was_needed()
    {
        var stats = CostCorpus.Stats;
        _ = CostCorpus.All;

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"CostR and RNet pairs {stats.Compared}, not bit-equal {stats.NotExact}, largest difference {stats.MaxDifference:R} {stats.Worst}"));
        stats.Compared.ShouldBeGreaterThan(0);
        stats.NotExact.ShouldBe(0L, "exact equality first; a tolerance is used only if a pair is not bit-equal, and it goes into ADR 0047");
    }
}
