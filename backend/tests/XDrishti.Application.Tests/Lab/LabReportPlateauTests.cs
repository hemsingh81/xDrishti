using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>CF-07 part C in the report: a plateau that fails or cannot be read warns (and says report only), a plateau that holds adds no warning, and the basis is among the honest limits.</summary>
public sealed class LabReportPlateauTests
{
    private static LabRunView Run() => LabRunView.Of(new LabRun(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), new string('1', 64), "LAB_TEST", 2, new string('a', 64),
        new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null),
        new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "robust", new string('c', 64), "sim-5", LabRunState.Succeeded, null, 0, 0, ["sk1"], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null));

    private static PlateauView Plateau(string status, bool enforce = false) => new(
        "ReportOnly", enforce, "ReportOnly/enforce=off/retention=0.7/minPoints=3", new string('9', 64), 0.70, 3, ">=", status, "reason", false, "in-sample sweep, basis text",
        [new PlateauParameterView("period", 10, 5, 0.15, 9, 0.133, 11, 0.133, 0.02, 10, 0.05, status == "fail" ? "spike" : "inconclusive", status, "r", [])]);

    private static LabVerdictView Verdict(PlateauView? plateau) =>
        new("MARGINAL", 2, 1.25, new EvidenceView(400, 0.4, 1.6, 6, 5, 8, 0, "walk_forward"), [new GateView("oos_trades", 300, 375, 400, true, "ok")], [], [], [], ["x"],
            Nulls: plateau is null ? null : new NullsView("ReportOnly", false, "s", "h", new RandomEntryView(40, 40, 1000, 1000, 0.1, 0, 0, 0, 0, 0, 95, 0, 0, 0.001, 0.05, "<=", "pass", "ok", 0, 0, "1"), false, "basis", [], Plateau: plateau));

    [Fact]
    public void A_failing_plateau_warns_names_the_parameter_and_says_report_only()
    {
        var r = LabReportBuilder.Build(Run(), Verdict(Plateau("fail")), 6);

        var w = r.Warnings.Single(x => x.Code == "plateau");
        w.Text.ShouldContain("period (spike)");
        w.Text.ShouldContain("in-sample sweep");
        w.Text.ShouldContain("report only");
    }

    [Fact]
    public void An_enforced_failing_plateau_does_not_say_report_only()
    {
        LabReportBuilder.Build(Run(), Verdict(Plateau("fail", enforce: true)), 6).Warnings.Single(x => x.Code == "plateau").Text.ShouldNotContain("report only");
    }

    [Fact]
    public void An_unavailable_plateau_warns_that_it_is_not_a_pass()
    {
        LabReportBuilder.Build(Run(), Verdict(Plateau("unavailable")), 6).Warnings.Single(x => x.Code == "plateau").Text.ShouldContain("is not a pass");
    }

    [Fact]
    public void A_plateau_that_holds_adds_no_warning_and_the_basis_is_among_the_limits()
    {
        var r = LabReportBuilder.Build(Run(), Verdict(Plateau("pass")), 6);

        r.Warnings.ShouldNotContain(x => x.Code == "plateau");
        r.HonestLimits.ShouldContain("in-sample sweep, basis text");
        r.Nulls!.Plateau!.Status.ShouldBe("pass");
    }

    [Fact]
    public void Without_the_member_the_report_has_none_of_it()
    {
        var r = LabReportBuilder.Build(Run(), Verdict(null), 6);

        r.Nulls.ShouldBeNull();
        r.Warnings.ShouldNotContain(x => x.Code == "plateau");
    }
}
