using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-07 (ADR 0208): the settings of the random-entry null. Report-only by default, enforcement off, every number in its range, and the text and hash that travel with a figure.</summary>
public sealed class NullOptionsTests
{
    [Fact]
    public void The_defaults_are_report_only_with_enforcement_off_and_the_specs_numbers()
    {
        var options = new NullOptions();

        (options.Mode, options.Enforce, options.Draws, options.Percentile, options.Redraws, options.Placebos, options.MinPlacebos).ShouldBe((ConfidenceMode.ReportOnly, false, 1000, 95d, 5, true, 19));
        options.IsOn.ShouldBeTrue();
        options.Alpha.ShouldBe(0.05);
        options.Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ConfidenceMode.Gate, 1000, 95d, 5)]
    [InlineData(ConfidenceMode.ReportOnly, 199, 95d, 5)]
    [InlineData(ConfidenceMode.ReportOnly, 5001, 95d, 5)]
    [InlineData(ConfidenceMode.ReportOnly, 1000, 89d, 5)]
    [InlineData(ConfidenceMode.ReportOnly, 1000, 99.5, 5)]
    [InlineData(ConfidenceMode.ReportOnly, 1000, double.NaN, 5)]
    [InlineData(ConfidenceMode.ReportOnly, 1000, 95d, -1)]
    [InlineData(ConfidenceMode.ReportOnly, 1000, 95d, 21)]
    public void A_value_out_of_range_is_a_problem(ConfidenceMode mode, int draws, double percentile, int redraws)
    {
        new NullOptions(mode, false, draws, percentile, redraws).Problems().ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(18)]
    [InlineData(21)]
    public void The_fewest_comparable_placebos_must_be_nineteen_or_twenty(int min)
    {
        new NullOptions(MinPlacebos: min).Problems().ShouldContain(p => p.Contains("MinPlacebos", StringComparison.Ordinal));
        new NullOptions(MinPlacebos: 19).Problems().ShouldBeEmpty();
        new NullOptions(MinPlacebos: 20).Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_placebo_switch_and_the_minimum_are_part_of_the_key_text_and_the_hash()
    {
        var on = new NullOptions();

        new NullOptions(Placebos: false).KeyText().ShouldContain("placebos=off");
        on.KeyText().ShouldEndWith("placebos=on/minPlacebos=19");
        new NullOptions(MinPlacebos: 18).SnapshotHash().ShouldNotBe(on.SnapshotHash());
        new NullOptions(Placebos: false).SnapshotHash().ShouldNotBe(on.SnapshotHash());
    }

    [Fact]
    public void Enforce_with_the_mode_off_is_a_problem_because_nothing_would_be_computed_to_enforce()
    {
        new NullOptions(ConfidenceMode.Off, Enforce: true).Problems().ShouldContain(p => p.Contains("Enforce", StringComparison.Ordinal));
        new NullOptions(ConfidenceMode.Off).Problems().ShouldBeEmpty();
        new NullOptions(ConfidenceMode.ReportOnly, Enforce: true).Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_key_text_and_hash_change_with_the_settings()
    {
        var on = new NullOptions(Enforce: true);
        var off = new NullOptions();

        on.KeyText().ShouldNotBe(off.KeyText());
        on.SnapshotHash().ShouldNotBe(off.SnapshotHash());
        off.SnapshotHash().Length.ShouldBe(64);
        new NullOptions(ConfidenceMode.Off).IsOn.ShouldBeFalse();
    }
}
