using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>CF-01: what the cell view says about effective n, for a row with and without the block, and the words on a screen.</summary>
public sealed class CellStatsViewDependenceTests
{
    private static CellStatsRow Row(DependenceAssessment? dependence, bool enough = true) =>
        new(new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR"), CellWindow.All, new DateOnly(2026, 3, 1), true, 34, 20, 30.5, 0.6, 0.5, 0.55, 0.4, 0.7, 1, -1, 0.2, 1.4, 3, 0.5, 1, 1, 2, enough, dependence);

    private static StoredCellStats Stored(CellStatsRow row) => new(new string('a', 64), DateTimeOffset.UnixEpoch, new CellStatsOptions(), "s", row);

    [Fact]
    public void A_row_without_the_block_reads_not_computed()
    {
        var view = CellStatsView.Of(Stored(Row(null)));

        view.Dependence.ShouldBeNull();
        view.Limits.ShouldBe(CellStatsView.LimitsText);
        view.IndependentN.ShouldBeNull();
        view.EffectiveN.ShouldBe(30.5);   // the recency-weight sum is untouched
    }

    [Fact]
    public void A_row_with_the_block_carries_it_and_the_words_n_34_effective_11_point_2()
    {
        var dep = new DependenceAssessment(ConfidenceMode.ReportOnly, 34, 11.2, 34 / 11.2, "cluster", 396, 0.512, 273, 828, 2.1, 75.6, false, false, string.Empty, 0.15);

        var view = CellStatsView.Of(Stored(Row(dep)));

        view.IndependentN.ShouldBe(11.2);
        view.Dependence!.NEff.ShouldBe(11.2);
        view.Dependence.Mode.ShouldBe("ReportOnly");
        view.Dependence.Summary.ShouldBe("n=34, effective 11.2; needs about 396 to see 0.15 R; smallest visible now 0.51 R; about 76 years at this rate (if the rate and the edge hold)");
        view.Limits.ShouldEndWith(DependenceView.LimitsText);
        view.Dependence.MinTrlDependent.ShouldBe(828);
        view.Evidence.ShouldBe("enough");
    }

    [Fact]
    public void An_unavailable_estimate_is_null_with_the_reason_never_a_number()
    {
        var dep = new DependenceAssessment(ConfidenceMode.ReportOnly, 4, null, null, "none", null, null, null, null, null, null, null, false, "unavailable: n < 5");

        var view = CellStatsView.Of(Stored(Row(dep)));

        view.Dependence!.NEff.ShouldBeNull();
        view.Dependence.Note.ShouldBe("unavailable: n < 5");
        view.Dependence.Summary.ShouldBe("n=4; effective n unavailable: n < 5");
    }

    [Fact]
    public void Gate_mode_names_effective_trades_in_the_not_enough_text()
    {
        var dep = new DependenceAssessment(ConfidenceMode.Gate, 34, 11, 3, "cluster", 396, 0.9, 273, 800, 2.1, 75.6, false, false, string.Empty);

        CellStatsView.Of(Stored(Row(dep, enough: false))).Evidence.ShouldContain("effective trades");
    }

    [Fact]
    public void Off_makes_no_view()
    {
        DependenceView.Of(new DependenceAssessment(ConfidenceMode.Off, 4, null, null, "none", null, null, null, null, null, null, null, false, string.Empty)).ShouldBeNull();
        DependenceView.Of(null).ShouldBeNull();
    }
}
