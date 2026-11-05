using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-12 part 12a (ADR 0218): the settings of the loop report. The spec's numbers, tighten-only floors where a looser value would weaken a bar, a hash that changes with every setting.</summary>
public sealed class LoopOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_numbers_and_usable()
    {
        var o = new LoopOptions();

        (o.MinDays, o.Draws, o.Level, o.HelpsAt, o.MinSlopePer50Rows, o.TargetGain, LoopOptions.SlopeWindow, o.SlopeLow, o.SlopeHigh, o.SlopeSe).ShouldBe((20, 2000, 0.90, 0.95, 0.002, 0.01, 200, 0.8, 1.2, 2.0));
        LoopOptions.HurtsAt.ShouldBe(0.05, 1e-12);
        LoopOptions.HurtsBar.ShouldBe(0.95, 1e-12);
        o.Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("MinDays", 19)]
    [InlineData("MinDays", 251)]
    [InlineData("Draws", 1999)]
    [InlineData("Draws", 20001)]
    [InlineData("Level", 0.89)]
    [InlineData("Level", 0.995)]
    [InlineData("HelpsAt", 0.94)]
    [InlineData("HelpsAt", 1)]
    [InlineData("MinSlopePer50Rows", 0)]
    [InlineData("MinSlopePer50Rows", 0.2)]
    [InlineData("TargetGain", 0.0005)]
    [InlineData("SlopeLow", 0.79)]
    [InlineData("SlopeLow", 1)]
    [InlineData("SlopeHigh", 1)]
    [InlineData("SlopeHigh", 1.21)]
    [InlineData("SlopeSe", 0.9)]
    [InlineData("SlopeSe", 2.1)]
    [InlineData("SlopeSe", double.NaN)]
    public void A_looser_or_out_of_range_value_is_a_problem(string name, double value)
    {
        var o = new LoopOptions();
        var bad = name switch
        {
            "MinDays" => o with { MinDays = (int)value },
            "Draws" => o with { Draws = (int)value },
            "Level" => o with { Level = value },
            "HelpsAt" => o with { HelpsAt = value },
            "MinSlopePer50Rows" => o with { MinSlopePer50Rows = value },
            "TargetGain" => o with { TargetGain = value },
            "SlopeLow" => o with { SlopeLow = value },
            "SlopeHigh" => o with { SlopeHigh = value },
            _ => o with { SlopeSe = value },
        };

        bad.Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_window_and_the_hurts_bar_are_fixed_not_settings()
    {
        typeof(LoopOptions).GetConstructors().Single().GetParameters().Select(p => p.Name).ShouldNotContain("SlopeWindow");
        typeof(LoopOptions).GetConstructors().Single().GetParameters().Select(p => p.Name).ShouldNotContain("HurtsBar");
        LoopOptions.SlopeWindow.ShouldBe(200);
    }

    [Fact]
    public void A_tighter_set_is_usable()
    {
        new LoopOptions(MinDays: 30, Draws: 5000, Level: 0.95, HelpsAt: 0.975, SlopeLow: 0.9, SlopeHigh: 1.1, SlopeSe: 1.5).Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_hash_changes_with_every_setting_and_not_otherwise()
    {
        var o = new LoopOptions();
        var baseline = o.SnapshotHash();

        new LoopOptions().SnapshotHash().ShouldBe(baseline);
        baseline.Length.ShouldBe(64);
        new[]
        {
            o with { MinDays = 21 }, o with { Draws = 2001 }, o with { Level = 0.91 }, o with { HelpsAt = 0.96 }, o with { MinSlopePer50Rows = 0.003 }, o with { TargetGain = 0.02 },
            o with { SlopeLow = 0.85 }, o with { SlopeHigh = 1.15 }, o with { SlopeSe = 1.9 },
        }.Select(x => x.SnapshotHash()).ShouldAllBe(h => h != baseline);
    }
}
