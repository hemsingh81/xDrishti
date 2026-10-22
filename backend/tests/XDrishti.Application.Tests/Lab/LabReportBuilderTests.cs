using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>Spec P5-06 criteria 2 to 4: the summary uses report numbers only, each warning fires on a hand-made document, the honest limits are always there.</summary>
public sealed partial class LabReportBuilderTests
{
    [GeneratedRegex(@"-?\d+(\.\d+)?")]
    private static partial Regex Number();

    private static LabRunView Run(string method = "full", string id = "LAB_TEST") => LabRunView.Of(new LabRun(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), new string('1', 64), id, 2, new string('a', 64),
        new LabUniverse("symbols", null, "symbols as listed", 1, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null),
        new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), method, new string('c', 64), "sim-5", LabRunState.Succeeded, null, 0, 0, ["sk1"], null, [], [], ["costs: charge sheet default"], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null));

    private static LabVerdictView Verdict(string outcome = "PASS", int trades = 400, double exp = 0.4, double pf = 1.6, double dd = 8, int folds = 6, int pos = 5, RobustnessView? rob = null, IReadOnlyList<CellView>? cells = null, IReadOnlyList<GateView>? gates = null) =>
        new(outcome, 2, 1.25, new EvidenceView(trades, exp, pf, folds, pos, dd, 0, "walk_forward"), gates ?? [new GateView("oos_trades", 300, 375, trades, trades >= 375, "ok")], [], cells ?? [Cell("15m|BUY", 200, 0.5), Cell("5m|BUY", 200, 0.3)], [], ["x"], rob);

    private static CellView Cell(string name, int trades, double? mean, bool? stable = true) => new(name, "15m", "BUY", "FIXED_RR{rr=2}", "FIXED_RR{rr=3}", null, stable, "ok", 5, 5, trades, mean, 0.1);

    private static LabReportView Build(LabVerdictView v, LabRunView? run = null) => LabReportBuilder.Build(run ?? Run(), v, 6);

    private static RobustnessView Robust(string verdict = "stable", double p = 0.01, double share = 0.01, IReadOnlyList<YearView>? years = null) => new(
        new SensitivityView("15m", "FIXED_RR{rr=2}", null, [new ParameterSensitivityView("rr", [], verdict, "r")], "in-sample"),
        new MonteCarloView(400, 1000, "1", 0.4, 8, 0.2, 0.4, 0.6, 5, 9, share),
        years ?? [new YearView(2025, 200, 0.4, 80, 1.5, true), new YearView(2026, 200, 0.4, 80, 1.5, true)],
        new SignFlipBaselineView("sign_flip", 400, 1000, "2", 0.4, 0.1, p, "lenient"), "unavailable: x", ["block limit"]);

    [Fact]
    public void A_clean_report_has_no_warnings_orders_cells_by_oos_mean_and_lists_what_it_does_not_hold()
    {
        var r = Build(Verdict(cells: [Cell("a", 200, 0.1), Cell("b", 200, 0.9), Cell("c", 200, null)]));

        r.Warnings.ShouldBeEmpty();
        r.BestCells.Select(c => c.Cell).ShouldBe(["b", "a", "c"]);
        r.NotIncluded.ShouldNotBeEmpty();
        r.Kpis.OosTrades.ShouldBe(400);
    }

    [Fact]
    public void Each_warning_rule_fires_on_its_own_condition()
    {
        Codes(Build(Verdict(trades: 100))).ShouldContain("low_samples");
        Codes(Build(Verdict(cells: [Cell("a", 10, 0.1), Cell("b", 390, 0.2)]))).ShouldContain("low_samples");
        Codes(Build(Verdict(exp: 0.05))).ShouldContain("cost_sensitivity");
        Codes(Build(Verdict(cells: [Cell("a", 300, 0.1), Cell("b", 100, 0.2)]))).ShouldContain("concentration");
        Codes(Build(Verdict(rob: Robust("fragile")))).ShouldContain("unstable_parameters");
        Codes(Build(Verdict(cells: [Cell("a", 200, 0.1, stable: false), Cell("b", 200, 0.2)]))).ShouldContain("unstable_parameters");
        Codes(Build(Verdict(rob: Robust(p: 0.2)))).ShouldContain("chance_not_excluded");
        Codes(Build(Verdict(rob: Robust(share: 0.2)))).ShouldContain("chance_not_excluded");
        Codes(Build(Verdict(rob: Robust(years: [new YearView(2025, 200, 0.9, 150, 2, true), new YearView(2026, 200, -0.1, -40, 0.8, false)])))).ShouldContain("concentration");
        Codes(Build(Verdict(rob: Robust()))).ShouldBeEmpty();
    }

    private static string[] Codes(LabReportView r) => [.. r.Warnings.Select(w => w.Code)];

    [Fact]
    public void The_honest_limits_always_carry_the_window_the_folds_against_the_minimum_and_for_a_robust_run_the_sign_flip_line()
    {
        var plain = Build(Verdict(folds: 4)).HonestLimits;
        var robust = Build(Verdict(rob: Robust()), Run("robust")).HonestLimits;

        plain.ShouldContain(l => l.Contains("2025-05-01 to 2026-04-30", StringComparison.Ordinal) && l.Contains("2.1 years", StringComparison.Ordinal));
        plain.ShouldContain("Walk-forward folds: 4; a PASS needs at least 6.");
        plain.ShouldContain(l => l.Contains("exploration", StringComparison.Ordinal));
        plain.ShouldNotContain(l => l.Contains("sign-flip", StringComparison.Ordinal));
        robust.ShouldContain(l => l.Contains("sign-flip baseline is lenient", StringComparison.Ordinal));
        robust.ShouldContain("block limit");
    }

    [Fact]
    public void The_summary_is_a_golden_text()
    {
        var r = Build(Verdict("MARGINAL", trades: 120, exp: 0.12, pf: 1.3, dd: 9.5, folds: 4, pos: 3));

        r.Summary.ShouldBe(
            "The full test of strategy LAB_TEST ended as MARGINAL. Out of sample it made 120 trades, an expectancy of 0.12 R, a profit factor of 1.3, a maximum drawdown of 9.5 R, with 3 positive folds of 4. "
            + "Gates missed: oos_trades. The family has 2 tested variants, so the thresholds were tightened by a factor of 1.25. The best cell was 15m|BUY with a mean of 0.5 R over 200 out-of-sample trades. "
            + "Warnings: low_samples, cost_sensitivity, concentration. This restates the numbers of this report, rests on a short history without a hold-out, and is not advice.");
    }

    [Fact]
    public void Every_number_in_the_summary_is_a_value_of_the_report()
    {
        var rng = new Random(2026);
        for (var i = 0; i < 200; i++)
        {
            var tiny = rng.Next(3) == 0;
            var rob = rng.Next(2) == 0 ? null : Robust(p: tiny ? rng.NextDouble() * 0.0001 : rng.NextDouble(), share: rng.NextDouble());
            var v = Verdict(rng.Next(3) == 0 ? "FAIL" : "PASS", rng.Next(0, 900), tiny ? -rng.NextDouble() * 0.004 : Math.Round(rng.NextDouble() - 0.3, 6), 1 + (rng.NextDouble() * 2), rng.NextDouble() * 30, rng.Next(1, 12), rng.Next(0, 12), rob,
                [Cell("15m|BUY", rng.Next(0, 500), rng.Next(4) == 0 ? null : rng.NextDouble()), Cell("5m|SELL", rng.Next(0, 500), rng.NextDouble())]);
            var report = Build(v, Run(rob is null ? "full" : "robust"));

            var allowed = Allowed(report);
            var text = report.Summary.Replace("LAB_TEST", string.Empty, StringComparison.Ordinal);
            foreach (Match m in Number().Matches(text))
            {
                allowed.ShouldContain(m.Value, $"'{m.Value}' in: {report.Summary}");
            }

            Regex.IsMatch(text, @"-0(?![.\d])").ShouldBeFalse(text);
        }
    }

    /// <summary>Every number of the report's JSON, rounded the way the summary prints it, plus the digit runs of its text.</summary>
    private static HashSet<string> Allowed(LabReportView report)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(report with { Summary = string.Empty }));
        Walk(doc.RootElement, set);
        return set;
    }

    private static void Walk(JsonElement e, HashSet<string> set)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Number:
                set.Add(Math.Round(e.GetDouble(), 2).ToString("0.##", CultureInfo.InvariantCulture));
                set.Add(Math.Round(e.GetDouble(), 4).ToString("0.####", CultureInfo.InvariantCulture));
                set.Add("0.0001");
                break;
            case JsonValueKind.String:
                foreach (Match m in Number().Matches(e.GetString()!))
                {
                    set.Add(m.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray())
                {
                    Walk(x, set);
                }

                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    Walk(p.Value, set);
                }

                break;
            default:
                break;
        }
    }

    [Fact]
    public void A_small_p_value_is_not_printed_as_zero_and_free_text_loses_control_characters()
    {
        var rob = Robust(p: 0.004);
        var report = Build(Verdict(rob: rob), Run(id: "A\u202EB\nC"));

        report.Summary.ShouldContain("p-value of 0.004.");
        report.Run.StrategyId.ShouldBe("AB C");
        report.Summary.ShouldNotContain("\u202E", Case.Sensitive);
        LabReportBuilder.P(0.00004).ShouldBe("<0.0001");
        LabReportBuilder.F(-0.003).ShouldBe("0");
    }
}
