using XDrishti.Domain.Lab;
using XDrishti.Domain.Paper;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>
/// Spec P10-04 on the hand-worked fixtures (a) to (c) and the properties of criterion 12. (a) and (b) are degenerate and hold exactly; (c) is random and holds within 0.04 of the analytic value (1.645 x 1.4142 / sqrt 60 = 0.3003).
/// </summary>
public sealed class BacktestBandTests
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static readonly BandOptions Draws = new(2_000);

    private static CellReference Reference(string cell, params (int Count, double R)[] parts) =>
        new(cell, [.. parts.SelectMany(p => Enumerable.Repeat(p.R, p.Count))]);

    private static List<PaperSlot> Paper(string cell, params (int Count, double R)[] parts) =>
        [.. parts.SelectMany(p => Enumerable.Repeat(new PaperSlot(cell, p.R), p.Count))];

    private static BandResult Run(List<PaperSlot> paper, params CellReference[] references) =>
        BacktestBand.Compute(paper, references, Draws, BacktestBand.SeedOf(1, "snap", paper.Count), BacktestBand.IntervalSeedOf(1, "snap", paper.Count))!;

    [Fact]
    public void Fixture_a_all_plus_half_against_all_plus_half_is_exactly_degenerate_and_both_statistics_are_met()
    {
        var band = Run(Paper(Cell, (60, 0.5)), Reference(Cell, (30, 0.5)));

        band.BandP05.ShouldBe(0.5);
        band.BandP95.ShouldBe(0.5);
        band.Position.ShouldBe("within");
        band.IntervalLow.ShouldBe(0.5);
        band.IntervalHigh.ShouldBe(0.5);
        band.PaperMaxDrawdown.ShouldBe(0);
        band.DrawdownP90.ShouldBe(0);
        band.DrawdownPosition.ShouldBe("within");
        band.Rank.ShouldBe(50);
        var criteria = PaperExitStatistics.Evaluate(60, true, true, band, null);
        criteria.Expectancy.Verdict.ShouldBe(CriterionResult.Met);
        criteria.Drawdown.Verdict.ShouldBe(CriterionResult.Met);
        criteria.EarlyWarning.ShouldBeNull();
    }

    [Fact]
    public void Fixture_b_all_minus_point_two_is_below_the_band_and_the_expectancy_is_not_met()
    {
        var band = Run(Paper(Cell, (60, -0.2)), Reference(Cell, (30, 0.5)));

        band.PaperMean.ShouldBe(-0.2, 1e-12);
        band.Position.ShouldBe("below");
        band.Rank.ShouldBe(0);
        var criteria = PaperExitStatistics.Evaluate(60, true, true, band, null);
        criteria.Expectancy.Verdict.ShouldBe(CriterionResult.NotMet);
        criteria.Expectancy.Label.ShouldContain("-0.200R is not above 0");
        criteria.Expectancy.Label.ShouldContain("below the backtest band [+0.500R, +0.500R]");
        criteria.EarlyWarning.ShouldBe(PaperExitStatistics.EarlyWarningText);
    }

    [Fact]
    public void Fixture_c_the_random_band_matches_the_analytic_value_and_the_paper_interval_includes_zero()
    {
        var band = Run(Paper(Cell, (24, 2.0), (36, -1.0)), Reference(Cell, (10, 2.0), (20, -1.0)));

        band.BandP05.ShouldBe(-0.3003, 0.04);
        band.BandP95.ShouldBe(0.3003, 0.04);
        band.PaperMean.ShouldBe(0.2, 1e-12);
        band.Position.ShouldBe("within");
        band.IntervalLow.ShouldBe(-0.115, 0.05);
        band.IntervalHigh.ShouldBe(0.515, 0.05);
        PaperExitStatistics.Evaluate(60, true, true, band, null).Expectancy.Label.ShouldContain("(point estimate; 90% interval [");
        PaperExitStatistics.Evaluate(60, true, true, band, null).Expectancy.Label.ShouldContain("includes 0: positive expectancy is not statistically established)");
    }

    [Fact]
    public void Nearest_rank_percentiles_follow_the_monte_carlo_rule()
    {
        double[] sorted = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        MonteCarlo.Percentile(sorted, 0.05).ShouldBe(1);
        MonteCarlo.Percentile(sorted, 0.90).ShouldBe(9);
        MonteCarlo.Percentile(sorted, 0.95).ShouldBe(10);

        var band = Run(Paper(Cell, (1, 0.0)), Reference(Cell, (1, 1.0), (1, 3.0)));
        band.BandP05.ShouldBe(1.0);
        band.BandP95.ShouldBe(3.0);
        band.Fan.Single().P05.ShouldBe(1.0);
        band.Fan.Single().P95.ShouldBe(3.0);
    }

    [Fact]
    public void The_seed_is_the_sha256_of_epoch_snapshot_and_n()
    {
        BacktestBand.SeedOf(7, "abc", 60).ShouldBe(2069585477776813650UL);
        BacktestBand.SeedOf(7, "abc", 61).ShouldNotBe(BacktestBand.SeedOf(7, "abc", 60));
    }

    [Fact]
    public void The_same_inputs_give_the_same_numbers()
    {
        var paper = Paper(Cell, (24, 2.0), (36, -1.0));
        var reference = Reference(Cell, (10, 2.0), (20, -1.0));

        var a = Run(paper, reference);
        var b = Run(paper, reference);

        (a.BandP05, a.BandP50, a.BandP95, a.Rank, a.DrawdownP90, a.IntervalLow).ShouldBe((b.BandP05, b.BandP50, b.BandP95, b.Rank, b.DrawdownP90, b.IntervalLow));
        a.Fan.Select(f => f.P50).ShouldBe(b.Fan.Select(f => f.P50));
    }

    [Fact]
    public void A_permutation_of_the_paper_trades_leaves_the_band_unchanged_and_changes_only_the_paper_fall()
    {
        const string Other = "ORB|15m|SELL|intraday|FIXED_RR|*";
        CellReference[] references = [Reference(Cell, (10, 2.0), (20, -1.0)), Reference(Other, (15, 1.0), (15, -0.5))];
        List<PaperSlot> paper = [.. Paper(Cell, (3, 2.0), (4, -1.0)), .. Paper(Other, (5, 1.0), (3, -0.5))];
        var shuffled = paper.OrderBy(s => s.R).ThenBy(s => s.CellKey, StringComparer.Ordinal).ToList();
        shuffled.SequenceEqual(paper).ShouldBeFalse();

        var a = Run(paper, references);
        var b = Run(shuffled, references);

        (a.BandP05, a.BandP50, a.BandP95, a.Rank, a.PaperMean, a.IntervalLow, a.IntervalHigh).ShouldBe((b.BandP05, b.BandP50, b.BandP95, b.Rank, b.PaperMean, b.IntervalLow, b.IntervalHigh));
        a.PaperMaxDrawdown.ShouldNotBe(b.PaperMaxDrawdown);
    }

    [Fact]
    public void A_reference_for_a_cell_the_paper_does_not_hold_changes_nothing()
    {
        var paper = Paper(Cell, (24, 2.0), (36, -1.0));
        var reference = Reference(Cell, (10, 2.0), (20, -1.0));

        var plain = Run(paper, reference);
        var more = Run(paper, reference, Reference("ORB|15m|SELL|swing|ATR|*", (40, 9.0)));

        more.BandP05.ShouldBe(plain.BandP05);
        more.BandP95.ShouldBe(plain.BandP95);
        more.DrawdownP90.ShouldBe(plain.DrawdownP90);
        more.Rank.ShouldBe(plain.Rank);
    }

    [Fact]
    public void The_fan_is_ordered_ends_at_the_paper_total_and_is_thinned_above_the_cap()
    {
        var band = Run(Paper(Cell, (24, 2.0), (36, -1.0)), Reference(Cell, (10, 2.0), (20, -1.0)));

        band.Fan.Count.ShouldBe(60);
        band.Fan.ShouldAllBe(f => f.P05 <= f.P50 && f.P50 <= f.P95);
        band.Fan[^1].Paper.ShouldBe(12.0, 1e-9);
        BacktestBand.FanIndexes(300).Length.ShouldBeLessThanOrEqualTo(BacktestBand.MaxFanPoints);
        BacktestBand.FanIndexes(300)[^1].ShouldBe(300);
    }

    [Fact]
    public void Nothing_is_drawn_without_paper_trades_a_reference_for_every_cell_or_a_valid_draw_count()
    {
        var reference = Reference(Cell, (30, 0.5));

        BacktestBand.Compute([], [reference], Draws, 1, 2).ShouldBeNull();
        BacktestBand.Compute(Paper("other|cell", (5, 1.0)), [reference], Draws, 1, 2).ShouldBeNull();
        BacktestBand.Compute(Paper(Cell, (5, 1.0)), [reference], new BandOptions(999), 1, 2).ShouldBeNull();
        BacktestBand.Compute(Paper(Cell, (5, 1.0)), [reference], new BandOptions(100_001), 1, 2).ShouldBeNull();
        BacktestBand.Compute(Paper(Cell, (5, 1.0)), [reference], new BandOptions(1_000), 1, 2).ShouldNotBeNull();
    }
}
