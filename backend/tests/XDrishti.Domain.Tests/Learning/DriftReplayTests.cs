using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The drift replay (spec P6-09): cells split into reference and monitored, proposals follow the alarms, nothing is recorded.</summary>
public sealed class DriftReplayTests
{
    private const long Day = 86_400;

    private static List<DriftObservation> Series(int count, Func<int, double> r) =>
        [.. Enumerable.Range(0, count).Select(i => new DriftObservation("t" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), i * Day, r(i)))];

    private static double Alternating(int i) => i % 2 == 0 ? 1.3 : -0.7;

    [Fact]
    public void A_decaying_cell_gets_a_pause_proposal_and_a_recovery_proposal()
    {
        var cell = new DriftCellInput("a|15m|BUY|intraday|X|*", Series(200, i => i < 40 ? Alternating(i) : i < 100 ? -0.5 : 1.0 + (0.5 * Alternating(i))));

        var report = DriftReplay.Run([cell], 40 * Day, new DriftOptions()).Value;

        var found = report.Cells.ShouldHaveSingleItem();
        found.Band.ShouldNotBeNull();
        found.MonitoredTrades.ShouldBe(160);
        report.Proposals.Count.ShouldBe(2);
        report.Proposals[0].Pause.ShouldBeTrue();
        report.Proposals[1].Pause.ShouldBeFalse();
        report.Proposals[0].AtEpochSeconds.ShouldBeLessThan(report.Proposals[1].AtEpochSeconds);
        report.ToText().ShouldContain("proposed (not recorded)");
    }

    [Fact]
    public void A_cell_with_a_short_reference_is_listed_and_not_monitored()
    {
        var cell = new DriftCellInput("a|15m|BUY|intraday|X|*", Series(60, Alternating));

        var report = DriftReplay.Run([cell], 20 * Day, new DriftOptions()).Value;

        report.Cells[0].Band.ShouldBeNull();
        report.Cells[0].NotMonitored.ShouldNotBeNull();
        report.Proposals.ShouldBeEmpty();
    }

    [Fact]
    public void The_report_text_is_the_same_for_the_same_input_in_any_order()
    {
        var a = new DriftCellInput("a|15m|BUY|intraday|X|*", Series(200, i => i < 40 ? Alternating(i) : -0.5));
        var b = new DriftCellInput("b|15m|BUY|intraday|X|*", Series(200, Alternating));

        var one = DriftReplay.Run([a, b], 40 * Day, new DriftOptions()).Value.ToText();
        var two = DriftReplay.Run([b with { Trades = [.. b.Trades.Reverse()] }, a], 40 * Day, new DriftOptions()).Value.ToText();

        two.ShouldBe(one);
    }

    [Fact]
    public void Bad_options_a_repeated_cell_and_a_non_number_are_refused()
    {
        var cell = new DriftCellInput("a", Series(60, Alternating));

        DriftReplay.Run([cell], 0, new DriftOptions(WindowTrades: 2)).IsFailure.ShouldBeTrue();
        DriftReplay.Run([cell, cell], 0, new DriftOptions()).IsFailure.ShouldBeTrue();
        DriftReplay.Run([cell with { Trades = [new DriftObservation("x", 1, double.NaN)] }], 0, new DriftOptions()).IsFailure.ShouldBeTrue();
        DriftReplay.Run([], 0, new DriftOptions()).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Calibration_outcomes_are_reported_with_their_note()
    {
        var outcomes = Enumerable.Range(0, 120).Select(i => new CalibrationObservation("k" + i, i * Day, 0.5, i % 2)).ToList();

        var report = DriftReplay.Run([], 0, new DriftOptions(), "model:7", outcomes, "only unseen trades").Value;

        report.Calibration!.Outcomes.ShouldBe(120);
        report.ToText().ShouldContain("model:7: 120 outcomes read. only unseen trades");
    }

    [Fact]
    public void Proposals_are_ordered_by_time_then_cell()
    {
        var late = new DriftCellInput("a|15m|BUY|intraday|X|*", Series(300, i => i < 100 ? Alternating(i) : i < 160 ? -1.0 : Alternating(i)));
        var early = new DriftCellInput("b|15m|BUY|intraday|X|*", Series(300, i => i < 40 ? Alternating(i) : i < 100 ? -1.0 : Alternating(i)));

        var report = DriftReplay.Run([late, early], 40 * Day, new DriftOptions()).Value;

        report.Proposals.Select(p => p.AtEpochSeconds).ShouldBe(report.Proposals.Select(p => p.AtEpochSeconds).Order());
        report.Proposals[0].CellKey.ShouldStartWith("b");
    }
}
