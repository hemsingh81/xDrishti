using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-05: the settings are the spec's proposals, report-only with Enforce off, range-checked, and a change of any value changes the recorded snapshot hash.</summary>
public sealed class CostStressOptionsTests
{
    [Fact]
    public void The_defaults_are_the_spec_proposals_report_only_and_not_enforced_and_valid()
    {
        var o = new CostStressOptions();

        o.Mode.ShouldBe(ConfidenceMode.ReportOnly);
        o.Enforce.ShouldBeFalse();
        (o.MidMultiplier, o.MidFloorR, o.HighMultiplier, o.HighFloorR, o.DelayMinutes, o.DelayRetention).ShouldBe((1.5, 0.08, 2.0, 0.0, 1, 0.70));
        o.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_snapshot_hash_is_stable_and_changes_with_any_value()
    {
        var a = new CostStressOptions();

        a.SnapshotHash().ShouldBe(new CostStressOptions().SnapshotHash());
        a.SnapshotHash().Length.ShouldBe(64);
        new[]
        {
            a with { Enforce = true }, a with { MidMultiplier = 1.6 }, a with { MidFloorR = 0.09 }, a with { HighMultiplier = 2.5 }, a with { HighFloorR = 0.01 }, a with { DelayMinutes = 2 }, a with { DelayRetention = 0.8 }, a with { Mode = ConfidenceMode.Off },
        }.Select(o => o.SnapshotHash()).Append(a.SnapshotHash()).Distinct().Count().ShouldBe(9);
    }

    [Fact]
    public void KeyText_is_the_invariant_text_of_the_values()
    {
        new CostStressOptions().KeyText().ShouldBe("ReportOnly/enforce=off/1.5>=0.08/2>0/delay=1min>=0.7");
        new CostStressOptions(Enforce: true).KeyText().ShouldContain("enforce=on");
    }

    [Fact]
    public void Gate_mode_is_not_a_mode_and_Off_is()
    {
        (new CostStressOptions(ConfidenceMode.Gate)).Problems().ShouldNotBeEmpty();
        new CostStressOptions(ConfidenceMode.Off).Problems().ShouldBeEmpty();
        new CostStressOptions(ConfidenceMode.Off).IsOn.ShouldBeFalse();
        ((ConfidenceMode)7).ShouldNotBe(ConfidenceMode.Off);
        new CostStressOptions((ConfidenceMode)7).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_tier_and_capacity_defaults_are_the_spec_figures_and_in_rupees_the_cuts_are_crores()
    {
        var o = new CostStressOptions();

        (o.TierACutCrore, o.TierBCutCrore, o.CapacityFillShare, o.CapacityDailyShare).ShouldBe((100m, 20m, 0.01m, 0.005m));
        o.TierACut.ShouldBe(1_000_000_000m);
        o.TierBCut.ShouldBe(200_000_000m);
    }

    [Fact]
    public void The_tier_settings_have_their_own_text_and_hash_so_the_stress_hash_stored_with_earlier_verdicts_does_not_move()
    {
        var a = new CostStressOptions();
        var changed = new[]
        {
            a with { TierACutCrore = 150m }, a with { TierBCutCrore = 25m }, a with { CapacityFillShare = 0.005m }, a with { CapacityDailyShare = 0.004m },
        };

        a.TierKeyText().ShouldBe("tierA>=100cr/tierB>=20cr/fill<=0.01/median<=0.005");
        a.TierSnapshotHash().Length.ShouldBe(64);
        changed.Select(o => o.TierSnapshotHash()).Append(a.TierSnapshotHash()).Distinct().Count().ShouldBe(5);
        changed.ShouldAllBe(o => o.SnapshotHash() == a.SnapshotHash() && o.KeyText() == a.KeyText());
    }

    [Theory]
    [InlineData(99.99, 20, 0.01, 0.005, "TierACutCrore")]
    [InlineData(50, 20, 0.01, 0.005, "TierACutCrore")]
    [InlineData(0, 20, 0.01, 0.005, "TierACutCrore")]
    [InlineData(-5, 20, 0.01, 0.005, "TierACutCrore")]
    [InlineData(100001, 20, 0.01, 0.005, "TierACutCrore")]
    [InlineData(100, 19.99, 0.01, 0.005, "TierBCutCrore")]
    [InlineData(100, 1, 0.01, 0.005, "TierBCutCrore")]
    [InlineData(100, 0, 0.01, 0.005, "TierBCutCrore")]
    [InlineData(100, 100, 0.01, 0.005, "TierBCutCrore")]
    [InlineData(100, 150, 0.01, 0.005, "TierBCutCrore")]
    [InlineData(100, 20, 0.0101, 0.005, "CapacityFillShare")]
    [InlineData(100, 20, 0.1, 0.005, "CapacityFillShare")]
    [InlineData(100, 20, 0, 0.005, "CapacityFillShare")]
    [InlineData(100, 20, 0.01, 0.0051, "CapacityDailyShare")]
    [InlineData(100, 20, 0.01, 0.1, "CapacityDailyShare")]
    [InlineData(100, 20, 0.01, 0, "CapacityDailyShare")]
    [InlineData(100, 20, 0.01, -0.001, "CapacityDailyShare")]
    public void A_tier_or_capacity_value_that_would_loosen_the_gate_or_is_out_of_range_is_a_problem_the_host_refuses_to_start_on(double aCut, double bCut, double fill, double daily, string setting)
    {
        var problems = new CostStressOptions(TierACutCrore: (decimal)aCut, TierBCutCrore: (decimal)bCut, CapacityFillShare: (decimal)fill, CapacityDailyShare: (decimal)daily).Problems();

        problems.ShouldNotBeEmpty();
        problems.ShouldContain(p => p.StartsWith("CostStress:" + setting, StringComparison.Ordinal));
    }

    [Fact]
    public void Tightening_a_tier_or_capacity_value_is_allowed_and_the_defaults_themselves_are_the_limits()
    {
        var a = new CostStressOptions();

        a.Problems().ShouldBeEmpty();
        (a with { TierACutCrore = 200m }).Problems().ShouldBeEmpty();
        (a with { TierACutCrore = 100_000m, TierBCutCrore = 99_999m }).Problems().ShouldBeEmpty();
        (a with { TierBCutCrore = 99.99m }).Problems().ShouldBeEmpty();
        (a with { CapacityFillShare = 0.0001m, CapacityDailyShare = 0.0001m }).Problems().ShouldBeEmpty();
        (CostStressOptions.DefaultTierACutCrore, CostStressOptions.DefaultTierBCutCrore, CostStressOptions.DefaultCapacityFillShare, CostStressOptions.DefaultCapacityDailyShare).ShouldBe((100m, 20m, 0.01m, 0.005m));
    }

    [Fact]
    public void The_fill_minute_read_bounds_default_to_5000_trades_and_60_seconds_and_do_not_move_any_stored_hash()
    {
        var a = new CostStressOptions();

        (a.FillMinuteMaxTrades, a.FillMinuteTimeoutSeconds).ShouldBe((5000, 60));
        a.Problems().ShouldBeEmpty();
        var changed = a with { FillMinuteMaxTrades = 10, FillMinuteTimeoutSeconds = 5 };
        (changed.SnapshotHash(), changed.TierSnapshotHash()).ShouldBe((a.SnapshotHash(), a.TierSnapshotHash()), "the bounds are the cost of a read, not a figure the verdict was judged by; the block records the bound it used");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(20_000, 600)]
    public void A_bound_inside_its_range_is_allowed_in_both_directions_because_a_smaller_bound_only_adds_unavailable_readings(int maxTrades, int seconds)
    {
        new CostStressOptions(FillMinuteMaxTrades: maxTrades, FillMinuteTimeoutSeconds: seconds).Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-1, 60, "FillMinuteMaxTrades")]
    [InlineData(20_001, 60, "FillMinuteMaxTrades")]
    [InlineData(int.MaxValue, 60, "FillMinuteMaxTrades")]
    [InlineData(5000, 0, "FillMinuteTimeoutSeconds")]
    [InlineData(5000, -1, "FillMinuteTimeoutSeconds")]
    [InlineData(5000, 601, "FillMinuteTimeoutSeconds")]
    public void A_bound_outside_its_range_is_a_problem_the_host_refuses_to_start_on(int maxTrades, int seconds, string setting)
    {
        new CostStressOptions(FillMinuteMaxTrades: maxTrades, FillMinuteTimeoutSeconds: seconds).Problems().ShouldContain(p => p.StartsWith("CostStress:" + setting, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1.0, 2.0, 0.08, 0.0, 1, 0.7)]
    [InlineData(0.5, 2.0, 0.08, 0.0, 1, 0.7)]
    [InlineData(1.5, 1.5, 0.08, 0.0, 1, 0.7)]
    [InlineData(1.5, 5.1, 0.08, 0.0, 1, 0.7)]
    [InlineData(double.NaN, 2.0, 0.08, 0.0, 1, 0.7)]
    [InlineData(1.5, 2.0, 1.1, 0.0, 1, 0.7)]
    [InlineData(1.5, 2.0, 0.08, -1.1, 1, 0.7)]
    [InlineData(1.5, 2.0, 0.08, double.NaN, 1, 0.7)]
    [InlineData(1.5, 2.0, 0.08, 0.0, 0, 0.7)]
    [InlineData(1.5, 2.0, 0.08, 0.0, 6, 0.7)]
    [InlineData(1.5, 2.0, 0.08, 0.0, 1, 0.09)]
    [InlineData(1.5, 2.0, 0.08, 0.0, 1, 1.01)]
    [InlineData(1.5, 2.0, 0.08, 0.0, 1, double.NaN)]
    public void A_value_out_of_range_is_a_problem(double mid, double high, double midFloor, double highFloor, int delay, double retention)
    {
        new CostStressOptions(ConfidenceMode.ReportOnly, false, mid, midFloor, high, highFloor, delay, retention).Problems().ShouldNotBeEmpty();
    }
}
