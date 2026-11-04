using System.Text.Json;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-05 criteria 7 to 9 in the report: without the block nothing changes, with it the rows, the label and the warnings are there and the outcome is the verdict's own.</summary>
public sealed class LabReportCostStressTests
{
    private static LabRunView Run() => LabRunView.Of(new LabRun(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), new string('1', 64), "LAB_TEST", 2, new string('a', 64),
        new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null),
        new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "robust", new string('c', 64), "sim-5", LabRunState.Succeeded, null, 0, 0, ["sk1"], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null));

    private static LabVerdictView Verdict(CostStressView? stress = null) =>
        new("MARGINAL", 2, 1.25, new EvidenceView(400, 0.4, 1.6, 6, 5, 8, 0, "walk_forward"), [new GateView("oos_trades", 300, 375, 400, true, "ok")], [], [], [], ["x"], null, null, stress);

    private static CostStressView Block(bool checkedSheet, string midStatus = "pass", string delayStatus = "pass") => new(
        "ReportOnly", false, "ReportOnly/enforce=off/1.5>=0.08/2>0/delay=1min>=0.7", new string('9', 64), checkedSheet, "fixture-a",
        [
            new CostStressRowView(1, 400, 0.4, 0.1, ">=", "pass", "r1"),
            new CostStressRowView(1.5, 400, 0.04, 0.08, ">=", midStatus, "r2"),
            new CostStressRowView(2, 400, -0.02, 0, ">", "fail", "r3"),
        ],
        "fail", new CostStressDelayView(1, 100, 90, 0.2, 0.13, 0.65, 0.7, delayStatus, "r4", "n"), false, "formula text", "first-order limit text");

    [Fact]
    public void Without_the_block_the_report_has_none_of_it()
    {
        var r = LabReportBuilder.Build(Run(), Verdict(), 6);

        r.CostStress.ShouldBeNull();
        r.Warnings.ShouldBeEmpty();
        r.HonestLimits.ShouldNotContain("formula text");
    }

    [Fact]
    public void With_the_block_the_report_carries_it_the_formula_and_the_limit_and_the_outcome_is_the_verdicts()
    {
        var r = LabReportBuilder.Build(Run(), Verdict(Block(true)), 6);

        r.CostStress.ShouldNotBeNull().Rows.Count.ShouldBe(3);
        r.Outcome.ShouldBe("MARGINAL");
        r.HonestLimits.ShouldContain(CostStress.Formula);
        r.HonestLimits.ShouldContain(CostStress.FirstOrderLimit);
    }

    [Fact]
    public void A_missed_stress_or_delay_gate_warns_and_says_it_is_report_only()
    {
        var w = LabReportBuilder.Build(Run(), Verdict(Block(true, "fail", "fail")), 6).Warnings.Single(x => x.Code == "cost_stress").Text;

        w.ShouldContain("1.5x stress");
        w.ShouldContain("2x stress");
        w.ShouldContain("fill-one-minute-later");
        w.ShouldContain("65 %");
        w.ShouldContain("report only");
    }

    [Fact]
    public void An_unchecked_sheet_warns_that_the_stress_gates_cannot_pass()
    {
        var codes = LabReportBuilder.Build(Run(), Verdict(Block(false, "unavailable", "unavailable")), 6).Warnings.Select(x => x.Code).ToList();

        codes.ShouldContain("costs_not_checked");
    }

    [Fact]
    public void A_checked_sheet_has_no_label()
    {
        LabReportBuilder.Build(Run(), Verdict(Block(true, "pass", "pass")), 6).Warnings.Select(x => x.Code).ShouldNotContain("costs_not_checked");
    }

    [Fact]
    public void Equal_inputs_give_equal_bytes()
    {
        var a = JsonSerializer.Serialize(LabReportBuilder.Build(Run(), Verdict(Block(true)), 6));
        var b = JsonSerializer.Serialize(LabReportBuilder.Build(Run(), Verdict(Block(true)), 6));

        a.ShouldBe(b);
    }
}
