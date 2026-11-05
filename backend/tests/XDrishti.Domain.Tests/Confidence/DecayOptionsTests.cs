using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-12 part 12a (ADR 0218): the settings of the decay monitor. The spec's numbers, tighten-only floors, a hash that changes with every setting.</summary>
public sealed class DecayOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_numbers_and_usable()
    {
        var o = new DecayOptions();

        (o.WindowTrades, o.Sigmas, o.QuarterFloorR).ShouldBe((60, 1.5, -0.05));
        o.Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(61, 1.5, -0.05)]
    [InlineData(19, 1.5, -0.05)]
    [InlineData(60, 1.6, -0.05)]
    [InlineData(60, 0, -0.05)]
    [InlineData(60, double.NaN, -0.05)]
    [InlineData(60, 1.5, -0.06)]
    [InlineData(60, 1.5, 0.01)]
    [InlineData(60, 1.5, double.PositiveInfinity)]
    public void A_looser_or_out_of_range_value_is_a_problem(int window, double sigmas, double floor)
    {
        new DecayOptions(window, sigmas, floor).Problems().ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(20, 0.5, 0)]
    [InlineData(40, 1.0, -0.02)]
    public void A_tighter_value_is_usable(int window, double sigmas, double floor)
    {
        new DecayOptions(window, sigmas, floor).Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_hash_changes_with_every_setting_and_not_otherwise()
    {
        var baseline = new DecayOptions().SnapshotHash();

        new DecayOptions().SnapshotHash().ShouldBe(baseline);
        baseline.Length.ShouldBe(64);
        new[] { new DecayOptions(WindowTrades: 50), new DecayOptions(Sigmas: 1.4), new DecayOptions(QuarterFloorR: -0.04) }.Select(o => o.SnapshotHash()).ShouldAllBe(h => h != baseline);
    }
}
