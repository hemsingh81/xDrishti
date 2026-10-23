using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The direction of a slippage change read from the stored fill rules (spec P6-11, review): not from typed text.</summary>
public sealed class FillCostComparisonTests
{
    private static string Fills(double ticks = 1, double frac = 0.1, double extra = 1, double entry = 0.1, string gap = "at_open") =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"entry_limit_percent\":{entry},\"slippage\":{{\"ticks\":{ticks},\"bar_range_frac\":{frac},\"stop_exit_extra_ticks\":{extra},\"gap_fill\":\"{gap}\"}}}}");

    [Fact]
    public void The_real_canonical_text_of_the_default_rules_reads_back()
    {
        var other = FillRules.Create(0.1, SlippageConfig.Default).Value.ToCanonicalJson();

        FillCostComparison.Compare(FillRules.Default.ToCanonicalJson(), other).Error.Message.ShouldContain("same slippage");
    }

    [Theory]
    [InlineData(2, 0.1, 1, FillCostChange.Dearer)]
    [InlineData(1, 0.2, 1, FillCostChange.Dearer)]
    [InlineData(1, 0.1, 2, FillCostChange.Dearer)]
    [InlineData(0, 0.1, 1, FillCostChange.Cheaper)]
    [InlineData(1, 0.05, 1, FillCostChange.Cheaper)]
    [InlineData(1, 0.1, 0, FillCostChange.Cheaper)]
    [InlineData(2, 0.05, 1, FillCostChange.Mixed)]
    public void The_direction_comes_from_the_stored_components(double ticks, double frac, double extra, FillCostChange expected) =>
        FillCostComparison.Compare(Fills(), Fills(ticks, frac, extra)).Value.ShouldBe(expected);

    [Fact]
    public void Only_the_slippage_may_differ()
    {
        FillCostComparison.Compare(Fills(), Fills(2, entry: 0.2)).Error.Message.ShouldContain("more than the slippage");
        FillCostComparison.Compare(Fills(), Fills(2, gap: "at_close")).Error.Message.ShouldContain("more than the slippage");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"slippage\":12}")]
    [InlineData("not json")]
    [InlineData("{\"entry_limit_percent\":0.1,\"slippage\":{\"ticks\":\"x\",\"bar_range_frac\":0.1,\"stop_exit_extra_ticks\":1,\"gap_fill\":\"at_open\"}}")]
    public void Fills_that_cannot_be_read_are_refused(string bad)
    {
        FillCostComparison.Compare(Fills(), bad).IsFailure.ShouldBeTrue();
        FillCostComparison.Compare(bad, Fills(2)).IsFailure.ShouldBeTrue();
    }
}
