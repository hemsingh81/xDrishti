using XDrishti.Domain.Replay;

namespace XDrishti.Domain.Tests.Replay;

/// <summary>SR-02: the rules of a sweep (2 to 12 distinct starts before the end) and its spread (AC 4).</summary>
public sealed class ReplaySweepTests
{
    private static readonly DateOnly End = new(2026, 10, 1);

    [Fact]
    public void The_spread_of_twelve_cagrs_is_min_median_max_and_ten_of_twelve_positive()
    {
        double[] cagrs = [-0.041, -0.012, 0.003, 0.020, 0.035, 0.044, 0.050, 0.061, 0.077, 0.090, 0.112, 0.146];
        double[] drawdowns = [12, 10, 9, 8, 8, 7, 7, 6, 5, 5, 4, 3];
        var points = cagrs.Select((c, i) => new ReplaySweepPoint(c, (decimal)drawdowns[i], i % 2 == 0)).ToList();

        var spread = ReplaySweepRules.Spread(points);

        spread.Starts.ShouldBe(12);
        spread.Cagr.ShouldNotBeNull();
        spread.Cagr.Min.ShouldBe(-0.041);
        spread.Cagr.Median.ShouldBe(0.047, 1e-9);
        spread.Cagr.Max.ShouldBe(0.146);
        spread.ShareCagrPositive!.Value.ShouldBe(10 / 12.0, 1e-9);
        spread.MaxDrawdownPct!.Min.ShouldBe(3);
        spread.MaxDrawdownPct.Median.ShouldBe(7);
        spread.MaxDrawdownPct.Max.ShouldBe(12);
        spread.ShareBeatingBenchmark!.Value.ShouldBe(0.5);
    }

    [Fact]
    public void A_figure_no_start_gave_is_null_not_zero()
    {
        var spread = ReplaySweepRules.Spread([new ReplaySweepPoint(null, null, null), new ReplaySweepPoint(null, null, null)]);

        (spread.Cagr, spread.MaxDrawdownPct, spread.ShareCagrPositive, spread.ShareBeatingBenchmark).ShouldBe((null, null, null, null));
        ReplaySweepRules.Spread([]).Starts.ShouldBe(0);
    }

    [Fact]
    public void Two_to_twelve_distinct_starts_before_the_end_are_usable()
    {
        ReplaySweepRules.Problems([End.AddDays(-300), End.AddDays(-200)], End).ShouldBeEmpty();
        ReplaySweepRules.Problems([.. Enumerable.Range(1, 12).Select(i => End.AddDays(-i * 20))], End).ShouldBeEmpty();
    }

    [Fact]
    public void A_thirteenth_date_one_date_a_duplicate_and_a_start_not_before_the_end_are_named()
    {
        ReplaySweepRules.Problems([.. Enumerable.Range(1, 13).Select(i => End.AddDays(-i * 20))], End).ShouldHaveSingleItem().ShouldContain("2 to 12");
        ReplaySweepRules.Problems([End.AddDays(-9)], End).ShouldHaveSingleItem().ShouldContain("2 to 12");
        ReplaySweepRules.Problems([End.AddDays(-9), End.AddDays(-9)], End).ShouldContain("the start dates must differ");
        ReplaySweepRules.Problems([End.AddDays(-9), End], End).ShouldHaveSingleItem().ShouldContain("2026-10-01");
    }

    [Fact]
    public void A_baseline_run_needs_frozen_mode_and_the_default_arm_is_selection()
    {
        var p = new ReplayParams(End.AddDays(-200), End, new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Frozen);

        p.Arm.ShouldBe(ReplayArm.Selection);
        p.Problems().ShouldBeEmpty();
        (p with { Arm = ReplayArm.Baseline }).Problems().ShouldBeEmpty();
        (p with { Arm = ReplayArm.Baseline, Mode = ReplayMode.Learning }).Problems().ShouldContain("the baseline arm runs in frozen mode only");
        HindsightLabels.For(p).ShouldNotContain(HindsightLabels.BaselineArm);
        HindsightLabels.For(p with { Arm = ReplayArm.Baseline }).ShouldContain(HindsightLabels.BaselineArm);
    }
}
