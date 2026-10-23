using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Counts per cell, the worst list, the growing-tag detector, determinism and refusals of the loss tag analyser (P6-15, ADR 0071).</summary>
public sealed class LossTagAnalyzerTests
{
    private static readonly DateOnly Start = new(2026, 1, 5);

    /// <summary>A loss on day <paramref name="day"/>: a plain stop-out, or one that reached +1.5 R first.</summary>
    private static LossTagInput Loss(string cell, int day, bool gaveBack = false, double r = -1)
    {
        var t = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Start.AddDays(day), ExitReason.Stop, entryMinute: 600, entry: 100, stop: 99, target: 102, exitPrice: 99, rExact: -1, rFilled: -1);
        t = t with { MaeR = 1, MfeR = gaveBack ? 1.5 : 0.2, ExitEpochSeconds = t.EntryEpochSeconds + 3600 };
        return new LossTagInput(cell, t, r);
    }

    private static LossTagInput Win(string cell, int day) =>
        new(cell, FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Start.AddDays(day), entryMinute: 600), 1.9);

    [Fact]
    public void Counts_are_per_cell_wins_are_not_losses_and_untagged_is_counted()
    {
        var untagged = Loss("B", 3) with { Trade = Loss("B", 3).Trade with { ExitReason = ExitReason.SquareOff, ExitPrice = 98.7, RExact = -1.3 } };
        var inputs = new[] { Loss("A", 0), Loss("A", 1, gaveBack: true), Win("A", 2), untagged };

        var report = LossTagAnalyzer.Analyze(inputs, new LossTagOptions()).Value;

        report.Trades.ShouldBe(4);
        report.Losses.ShouldBe(3);
        var a = report.Cells[0];
        a.CellKey.ShouldBe("A");
        (a.Trades, a.Losses, a.Untagged).ShouldBe((3, 2, 0));
        a.Counts[(int)LossTagKind.NormalVariance].ShouldBe(1);
        a.Counts[(int)LossTagKind.GaveBackProfit].ShouldBe(1);
        var b = report.Cells[1];
        (b.Losses, b.Untagged).ShouldBe((1, 1));
        b.Counts.ShouldAllBe(n => n == 0);
    }

    [Fact]
    public void The_worst_losses_are_listed_lowest_first_with_their_numbers()
    {
        var inputs = new[] { Loss("A", 0, r: -1), Loss("A", 1, r: -2.5), Loss("A", 2, r: -1.5) };

        var report = LossTagAnalyzer.Analyze(inputs, new LossTagOptions(ShowWorst: 2)).Value;

        report.Worst.Select(w => w.R).ShouldBe([-2.5, -1.5]);
        report.ToText().ShouldContain("NormalVariance: stopped out at -1.00 R");
        report.ToText().ShouldContain("not built: against_regime");
        report.ToText().ShouldContain("not built: news_results");
    }

    [Fact]
    public void A_tag_that_rises_between_two_windows_of_losses_is_growing()
    {
        var options = new LossTagOptions(GrowingWindow: 10, GrowingMinCount: 4, GrowingMinRise: 0.2);
        var inputs = Enumerable.Range(0, 20).Select(i => Loss("A", i, gaveBack: i >= 14)).ToList();

        var report = LossTagAnalyzer.Analyze(inputs, options).Value;

        var growing = report.Cells[0].Growing.ShouldHaveSingleItem();
        (growing.Kind, growing.EarlierCount, growing.RecentCount).ShouldBe((LossTagKind.GaveBackProfit, 0, 6));
        growing.RecentShare.ShouldBe(0.6);
        report.Comparisons.ShouldBe(6);
        report.Limits.ShouldContain(l => l.Contains("6 cell and tag pairs") && l.Contains("at least 20 losses") && l.Contains("0.3 flags"));
        report.ToText().ShouldContain("growing: GaveBackProfit 0/10 -> 6/10");
    }

    [Fact]
    public void Too_few_losses_or_too_small_a_rise_flags_nothing_and_normal_variance_never_grows()
    {
        var options = new LossTagOptions(GrowingWindow: 10, GrowingMinCount: 4, GrowingMinRise: 0.2);
        LossTagAnalyzer.Analyze(Enumerable.Range(0, 19).Select(i => Loss("A", i, gaveBack: true)).ToList(), options).Value.Cells[0].Growing.ShouldBeEmpty();
        LossTagAnalyzer.Analyze(Enumerable.Range(0, 20).Select(i => Loss("A", i, gaveBack: i % 2 == 0)).ToList(), options).Value.Cells[0].Growing.ShouldBeEmpty();
        var late = Enumerable.Range(0, 20).Select(i => Loss("A", i, gaveBack: i < 10)).ToList();
        LossTagAnalyzer.Analyze(late, options).Value.Cells[0].Growing.ShouldBeEmpty();
    }

    [Fact]
    public void The_same_trades_in_any_order_give_the_same_text()
    {
        var inputs = Enumerable.Range(0, 25).Select(i => Loss(i % 2 == 0 ? "A" : "B", i, gaveBack: i % 3 == 0, r: -1 - (i % 5 * 0.1))).ToList();
        var options = new LossTagOptions(GrowingWindow: 5, GrowingMinCount: 2);

        var forward = LossTagAnalyzer.Analyze(inputs, options).Value.ToText();
        var shuffled = LossTagAnalyzer.Analyze([.. inputs.AsEnumerable().Reverse()], options).Value.ToText();

        shuffled.ShouldBe(forward);
    }

    [Fact]
    public void Bad_input_is_a_failure_with_a_code()
    {
        var one = Loss("A", 0);
        LossTagAnalyzer.Analyze([], new LossTagOptions()).Error.Code.ShouldBe(LossTagAnalyzer.InvalidCode);
        LossTagAnalyzer.Analyze([one, one], new LossTagOptions()).Error.Message.ShouldContain("twice");
        LossTagAnalyzer.Analyze([one with { R = double.NaN }], new LossTagOptions()).IsFailure.ShouldBeTrue();
        LossTagAnalyzer.Analyze([one], new LossTagOptions(GrowingWindow: 2)).Error.Message.ShouldContain("growing_window");
        LossTagAnalyzer.Analyze([one], new LossTagOptions(GapMinR: double.NaN)).IsFailure.ShouldBeTrue();
    }
}
