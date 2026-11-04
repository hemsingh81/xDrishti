using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>CF-02: what the cell view says about the shrunk figures, for a row with and without them.</summary>
public sealed class CellStatsViewShrinkageTests
{
    private static CellStatsRow Row(ShrinkageAssessment? shrinkage) =>
        new(new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR"), CellWindow.All, new DateOnly(2026, 3, 1), true, 10, 6, 9.5, 0.6, 0.5, 0.55, 0.4, 0.7, 1, -1, 0.4, 1.4, 3, 0.5, 1, 1, 2, false, null, shrinkage);

    private static StoredCellStats Stored(CellStatsRow row) => new(new string('a', 64), DateTimeOffset.UnixEpoch, new CellStatsOptions(), "s", row);

    [Fact]
    public void A_row_without_the_figures_reads_not_computed_and_the_limits_are_the_old_text()
    {
        var view = CellStatsView.Of(Stored(Row(null)));

        view.Shrinkage.ShouldBeNull();
        view.Limits.ShouldBe(CellStatsView.LimitsText);
        view.ExpectancyR.ShouldBe(0.4);   // the raw figure is there as it was
    }

    [Fact]
    public void A_row_with_the_figures_carries_them_beside_the_raw_ones_and_the_limit_is_printed()
    {
        var a = new ShrinkageAssessment(ConfidenceMode.ReportOnly, "strategy", 0.1, 0.04, "estimated", 0.2174, 0.1652, -0.1258, 20, "default", 0.53, 10, "n is the recency-weight n alone (effective n unavailable)");

        var view = CellStatsView.Of(Stored(Row(a)));

        view.Shrinkage!.ShrunkMeanR.ShouldBe(0.1652);
        view.Shrinkage.ShrunkLowerR.ShouldBe(-0.1258);
        view.Shrinkage.ParentLevel.ShouldBe("strategy");
        view.Shrinkage.TauSource.ShouldBe("estimated");
        view.Shrinkage.StrengthSource.ShouldBe("default");
        view.Shrinkage.Mode.ShouldBe("ReportOnly");
        view.ExpectancyR.ShouldBe(0.4);
        view.Enough.ShouldBeFalse();
        view.Evidence.ShouldBe("not enough evidence (fewer trades than the minimum)");   // the label does not change in ReportOnly
        view.Limits.ShouldEndWith(ShrinkageView.LimitsText);
        ShrinkageView.LimitsText.ShouldContain("ignores the uncertainty in the pooling parameters");
        ShrinkageView.LimitsText.ShouldContain("not a forecast");
    }

    [Fact]
    public void An_unavailable_assessment_is_all_null_with_the_reason()
    {
        var view = CellStatsView.Of(Stored(Row(ShrinkageAssessment.Unavailable(ConfidenceMode.ReportOnly, "no parent with 30 trades", 9.5))));

        view.Shrinkage!.ShrunkMeanR.ShouldBeNull();
        view.Shrinkage.ShrunkLowerR.ShouldBeNull();
        view.Shrinkage.ParentLevel.ShouldBeNull();
        view.Shrinkage.Note.ShouldBe("unavailable: no parent with 30 trades");
    }

    [Fact]
    public void Off_makes_no_view()
    {
        ShrinkageView.Of(ShrinkageAssessment.Unavailable(ConfidenceMode.Off, "x")).ShouldBeNull();
        ShrinkageView.Of(null).ShouldBeNull();
    }
}
