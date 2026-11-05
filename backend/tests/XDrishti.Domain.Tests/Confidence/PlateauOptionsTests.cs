using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-07 part C (ADR 0210): the settings of the plateau gate. Report-only by default, enforcement off, the spec's numbers, every number in its range.</summary>
public sealed class PlateauOptionsTests
{
    [Fact]
    public void The_defaults_are_report_only_with_enforcement_off_and_the_specs_numbers()
    {
        var options = new PlateauOptions();

        (options.Mode, options.Enforce, options.Retention, options.MinPoints).ShouldBe((ConfidenceMode.ReportOnly, false, 0.70, 3));
        options.IsOn.ShouldBeTrue();
        options.Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0.49, 3)]
    [InlineData(0.96, 3)]
    [InlineData(double.NaN, 3)]
    [InlineData(0.70, 2)]
    [InlineData(0.70, 6)]
    public void A_value_out_of_range_is_a_problem(double retention, int minPoints)
    {
        new PlateauOptions(Retention: retention, MinPoints: minPoints).Problems().ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(0.50, 3)]
    [InlineData(0.95, 5)]
    public void The_ends_of_the_ranges_are_allowed(double retention, int minPoints)
    {
        new PlateauOptions(Retention: retention, MinPoints: minPoints).Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_gate_mode_is_not_a_mode_and_enforce_with_the_mode_off_is_a_problem()
    {
        new PlateauOptions(ConfidenceMode.Gate).Problems().ShouldContain(p => p.Contains("Mode", StringComparison.Ordinal));
        new PlateauOptions(ConfidenceMode.Off, Enforce: true).Problems().ShouldContain(p => p.Contains("Enforce", StringComparison.Ordinal));
        new PlateauOptions(ConfidenceMode.Off).Problems().ShouldBeEmpty();
        new PlateauOptions(ConfidenceMode.Off).IsOn.ShouldBeFalse();
    }

    [Fact]
    public void The_key_text_and_hash_change_with_every_setting()
    {
        var baseline = new PlateauOptions();

        baseline.KeyText().ShouldBe("ReportOnly/enforce=off/retention=0.7/minPoints=3");
        baseline.SnapshotHash().Length.ShouldBe(64);
        new PlateauOptions(Enforce: true).SnapshotHash().ShouldNotBe(baseline.SnapshotHash());
        new PlateauOptions(Retention: 0.8).SnapshotHash().ShouldNotBe(baseline.SnapshotHash());
        new PlateauOptions(MinPoints: 4).SnapshotHash().ShouldNotBe(baseline.SnapshotHash());
    }
}
