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
