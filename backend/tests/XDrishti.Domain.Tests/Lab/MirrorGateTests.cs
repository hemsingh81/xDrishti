using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-07 part B, criterion 6 (ADR 0209): the mirror gate by hand. A mirror at or below 0 passes; a positive mirror reads "mostly drift".</summary>
public sealed class MirrorGateTests
{
    [Fact]
    public void A_real_mean_of_plus_012_with_a_mirror_of_minus_010_passes()
    {
        var m = MirrorGate.Evaluate(0.12, -0.10, 300, 30);

        m.Status.ShouldBe(NullStatus.Pass);
        (m.Threshold, m.Trades, m.MirrorMeanR, m.RealMeanR).ShouldBe((0.0, 300, -0.10, 0.12));
    }

    [Fact]
    public void A_mirror_of_plus_008_fails_and_reads_mostly_drift()
    {
        var m = MirrorGate.Evaluate(0.12, 0.08, 300, 30);

        m.Status.ShouldBe(NullStatus.Fail);
        m.Reason.ShouldContain("mostly drift");
    }

    [Fact]
    public void A_mirror_of_exactly_zero_passes()
    {
        MirrorGate.Evaluate(0.12, 0.0, 300, 30).Status.ShouldBe(NullStatus.Pass);
    }

    [Theory]
    [InlineData(null, 300, 30)]
    [InlineData(-0.1, 29, 30)]
    [InlineData(double.NaN, 300, 30)]
    public void A_mirror_that_cannot_be_read_is_unavailable_and_never_a_pass(double? mirror, int placed, int min)
    {
        var m = MirrorGate.Evaluate(0.12, mirror, placed, min);

        m.Status.ShouldBe(NullStatus.Unavailable);
        m.MirrorMeanR.ShouldBeNull();
        m.Reason.ShouldStartWith("unavailable:");
    }

    [Fact]
    public void A_real_mean_that_is_not_finite_is_unavailable()
    {
        MirrorGate.Evaluate(double.NaN, -0.1, 300, 30).Status.ShouldBe(NullStatus.Unavailable);
    }
}
