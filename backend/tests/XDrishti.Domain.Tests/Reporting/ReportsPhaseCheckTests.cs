using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>Spec P8-17: the hand tables of the P8 phase check. A criterion that could not be read is never MET; MET needs real evidence for every counted criterion; the owner's stopwatch is supplied, never invented.</summary>
public sealed class ReportsPhaseCheckTests
{
    private static readonly ReportsPhaseTiming[] Fast = [new("ListReportRuns", true, 20, 10), new("GetReportWhatWorks", true, 300, 120)];

    private static readonly ReportsPhaseStopwatch Good = new([new(84, 3), new(97, 3), new(71, 4)], new DateOnly(2026, 10, 30));

    private static readonly ReportsPhaseRoute[] Routes = [new("GET", "/api/reports/runs"), new("GET", "/api/learning/overview"), new("POST", "/api/portfolio/trades")];

    private static ReportsPhaseFacts Facts(
        int runs = 3,
        IReadOnlyList<ReportsPhaseTiming>? timings = null,
        bool? interaction = true,
        ReportsPhaseStopwatch? stopwatch = null,
        IReadOnlyList<ReportsPhaseReportFact>? reports = null,
        IReadOnlyList<string>? breaks = null,
        int metrics = 40,
        bool overlap = true,
        bool mixed = true,
        bool? defaultSet = true,
        IReadOnlyList<ReportsPhaseRoute>? routes = null,
        bool? deterministic = true,
        IReadOnlyList<string>? sensitive = null) =>
        new(
            runs,
            runs * 100,
            2,
            ReportsPhaseAnswer.NoRuns,
            timings ?? Fast,
            reports ?? [new("summary", true, true, null), new("what-works", true, true, null)],
            metrics,
            breaks ?? [],
            overlap,
            mixed,
            defaultSet,
            routes ?? Routes,
            deterministic,
            null,
            sensitive ?? [],
            interaction,
            stopwatch);

    private static PhaseCriterion Of(ReportsPhaseFacts facts, int number) => ReportsPhaseCheck.Evaluate(facts).Criteria.Single(c => c.Number == number);

    [Fact]
    public void It_numbers_criteria_one_to_ten_as_in_the_plan()
    {
        ReportsPhaseCheck.Evaluate(Facts()).Criteria.Select(c => c.Number).ShouldBe(Enumerable.Range(1, 10));
    }

    [Theory]
    // server timing x interaction test x stopwatch: only a complete set of passes is MET; a fail is NOT MET; a gap is NOT EVALUABLE
    [InlineData("ok", "pass", "ok", PhaseVerdict.Met)]
    [InlineData("ok", "pass", "missing", PhaseVerdict.NotEvaluable)]
    [InlineData("ok", "pass", "over", PhaseVerdict.NotMet)]
    [InlineData("ok", "fail", "ok", PhaseVerdict.NotMet)]
    [InlineData("ok", "missing", "ok", PhaseVerdict.NotEvaluable)]
    [InlineData("ok", "missing", "missing", PhaseVerdict.NotEvaluable)]
    [InlineData("over", "pass", "ok", PhaseVerdict.NotMet)]
    [InlineData("missing", "pass", "ok", PhaseVerdict.NotEvaluable)]
    [InlineData("missing", "missing", "missing", PhaseVerdict.NotEvaluable)]
    [InlineData("ok", "pass", "toomany", PhaseVerdict.NotMet)]
    [InlineData("ok", "pass", "two", PhaseVerdict.NotEvaluable)]
    public void The_exit_question_follows_the_nine_way_table(string server, string interaction, string stopwatch, PhaseVerdict expected)
    {
        var timings = server switch
        {
            "ok" => Fast,
            "over" => [new ReportsPhaseTiming("GetReportWhatWorks", true, 1500, 200)],
            _ => [new ReportsPhaseTiming("GetReportWhatWorks", true, null, null)],
        };
        var watch = stopwatch switch
        {
            "ok" => Good,
            "over" => new ReportsPhaseStopwatch([new(84, 3), new(121, 3), new(71, 3)], new DateOnly(2026, 10, 30)),
            "toomany" => new ReportsPhaseStopwatch([new(84, 3), new(90, 5), new(71, 3)], new DateOnly(2026, 10, 30)),
            "two" => new ReportsPhaseStopwatch([new(84, 3), new(90, 3)], new DateOnly(2026, 10, 30)),
            _ => null,
        };
        var criterion = Of(Facts(timings: timings, interaction: interaction switch { "pass" => true, "fail" => false, _ => null }, stopwatch: watch), 1);
        criterion.Verdict.ShouldBe(expected);
        if (expected == PhaseVerdict.Met)
        {
            criterion.Caveat.ShouldNotBeNull();
        }
    }

    [Fact]
    public void Without_a_stored_run_criteria_one_two_and_three_are_not_evaluable_and_four_and_five_still_run()
    {
        var report = ReportsPhaseCheck.Evaluate(Facts(runs: 0, stopwatch: Good));
        report.Criteria.Where(c => c.Number <= 3).ShouldAllBe(c => c.Verdict == PhaseVerdict.NotEvaluable && c.Detail.Contains("no stored run", StringComparison.Ordinal));
        report.Criteria[3].Verdict.ShouldBe(PhaseVerdict.Met);
        report.Criteria[3].Caveat.ShouldNotBeNull();
        report.Criteria[4].Verdict.ShouldBe(PhaseVerdict.Met);
        report.ToText().ShouldContain("no stored run");
    }

    [Fact]
    public void An_unreadable_report_is_never_evidence()
    {
        var unread = Of(Facts(reports: [new("summary", true, true, null), new("pivot", false, false, "reports.too_large")]), 2);
        unread.Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        unread.Detail.ShouldContain("pivot");
        Of(Facts(reports: [new("summary", true, true, null), new("groups", true, false, "missing limits")]), 2).Verdict.ShouldBe(PhaseVerdict.NotMet);
        Of(Facts(reports: []), 2).Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        Of(Facts(), 2).Verdict.ShouldBe(PhaseVerdict.Met);
    }

    [Fact]
    public void A_figure_that_breaks_the_state_rule_is_not_met_with_its_path_and_nothing_scanned_is_not_evaluable()
    {
        var broken = Of(Facts(breaks: ["summary.All.WinRate: a number in state insufficient"]), 3);
        broken.Verdict.ShouldBe(PhaseVerdict.NotMet);
        broken.Detail.ShouldContain("summary.All.WinRate");
        Of(Facts(metrics: 0), 3).Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        var met = Of(Facts(), 3);
        met.Verdict.ShouldBe(PhaseVerdict.Met);
        met.Caveat!.ShouldContain("per-screen tests");
        Of(Facts(reports: [new("summary", false, false, "x")]), 3).Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
    }

    [Theory]
    [InlineData(true, true, true, PhaseVerdict.Met)]
    [InlineData(false, true, true, PhaseVerdict.NotMet)]
    [InlineData(true, false, true, PhaseVerdict.NotMet)]
    [InlineData(true, true, false, PhaseVerdict.NotMet)]
    public void Double_counting_needs_both_probes_refused_and_a_valid_default_set(bool overlap, bool mixed, bool defaultSet, PhaseVerdict expected)
    {
        Of(Facts(overlap: overlap, mixed: mixed, defaultSet: defaultSet), 4).Verdict.ShouldBe(expected);
    }

    [Fact]
    public void An_unread_default_set_is_not_evaluable_when_runs_exist()
    {
        Of(Facts(defaultSet: null), 4).Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
    }

    [Fact]
    public void The_engine_refuses_the_two_probe_sets()
    {
        ReportsPhaseProbes.OverlapRefused().ShouldBeTrue();
        ReportsPhaseProbes.MixedBasisRefused().ShouldBeTrue();
    }

    [Fact]
    public void A_write_route_under_a_guarded_prefix_is_not_met_unless_approved_and_other_prefixes_are_ignored()
    {
        Of(Facts(), 5).Verdict.ShouldBe(PhaseVerdict.Met);
        var planted = Of(Facts(routes: [.. Routes, new("POST", "/api/reports/export")]), 5);
        planted.Verdict.ShouldBe(PhaseVerdict.NotMet);
        planted.Detail.ShouldContain("POST /api/reports/export");
        Of(Facts(routes: [new("DELETE", "/api/plans/notices/{id}"), new("GET", "/api/reports/runs")]), 5).Verdict.ShouldBe(PhaseVerdict.NotMet);
        Of(Facts(routes: [new("ANY", "/api/exports/x"), new("GET", "/api/reports/runs")]), 5).Verdict.ShouldBe(PhaseVerdict.NotMet);
        Of(Facts(routes: [new("POST", "/api/reportsfoo"), new("GET", "/api/reports/runs")]), 5).Verdict.ShouldBe(PhaseVerdict.Met);
    }

    [Fact]
    public void A_host_without_a_route_table_or_with_no_guarded_route_cannot_judge_read_only()
    {
        var none = ReportsPhaseCheck.Evaluate(Facts() with { Routes = null });
        none.Criteria[4].Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        ReportsPhaseCheck.Evaluate(Facts(routes: [new("GET", "/api/other")])).Criteria[4].Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
    }

    [Fact]
    public void Layers_bundle_browser_and_gates_are_never_met_by_the_server()
    {
        var report = ReportsPhaseCheck.Evaluate(Facts(stopwatch: Good));
        foreach (var n in new[] { 6, 7, 8, 10 })
        {
            report.Criteria.Single(c => c.Number == n).Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        }
    }

    [Fact]
    public void Determinism_and_security_read_the_repeat_read_and_the_field_names()
    {
        Of(Facts(), 9).Verdict.ShouldBe(PhaseVerdict.Met);
        Of(Facts(), 9).Caveat!.ShouldContain("names of fields");
        Of(Facts(deterministic: false), 9).Verdict.ShouldBe(PhaseVerdict.NotMet);
        Of(Facts(sensitive: ["ClientCode"]), 9).Verdict.ShouldBe(PhaseVerdict.NotMet);
        Of(Facts(deterministic: null), 9).Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        Of(Facts(runs: 0), 9).Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
    }

    [Fact]
    public void The_metric_rule_holds_hand_cases()
    {
        ReportsMetricRule.Break("value", 1.0, null).ShouldBeNull();
        ReportsMetricRule.Break("provisional", -2.0, "one run provisional").ShouldBeNull();
        ReportsMetricRule.Break("value", double.NaN, null).ShouldNotBeNull();
        ReportsMetricRule.Break("value", null, null).ShouldNotBeNull();
        ReportsMetricRule.Break("insufficient", 0.0, "n 3").ShouldNotBeNull();
        ReportsMetricRule.Break("unavailable", null, " ").ShouldNotBeNull();
        ReportsMetricRule.Break("not_evaluable", null, "no costs").ShouldBeNull();
        ReportsMetricRule.Break("empty", null, "no trade").ShouldBeNull();
        ReportsMetricRule.Break("good", null, "x").ShouldNotBeNull();
    }

    private static PhaseCriterion C(int n, PhaseVerdict v, string? caveat = null) => new(n, "c", v, "d", caveat);

    private static List<PhaseCriterion> All(PhaseVerdict v) => [.. Enumerable.Range(1, 10).Select(n => C(n, v))];

    [Fact]
    public void The_headline_is_met_only_when_every_counted_criterion_is_met_and_none_is_not_met()
    {
        var all = All(PhaseVerdict.Met);
        ReportsPhaseReport.HeadlineOf(all).ShouldBe("P8 exit: MET");
        // 6, 9 and 10 may stay not evaluable
        var rest = all.Select(c => c.Number is 6 or 9 or 10 ? C(c.Number, PhaseVerdict.NotEvaluable) : c).ToList();
        ReportsPhaseReport.HeadlineOf(rest).ShouldStartWith("P8 exit: MET");
        // a MET resting on a convention carries its caveat
        rest[0] = C(1, PhaseVerdict.Met, "stopwatch is the owner's own");
        ReportsPhaseReport.HeadlineOf(rest).ShouldContain("stopwatch is the owner's own");
    }

    [Fact]
    public void The_headline_is_not_met_on_any_not_met_and_not_evaluable_on_any_gap()
    {
        var one = All(PhaseVerdict.Met);
        one[5] = C(6, PhaseVerdict.NotMet);
        ReportsPhaseReport.HeadlineOf(one).ShouldBe("P8 exit: NOT MET");
        var gap = All(PhaseVerdict.Met);
        gap[0] = C(1, PhaseVerdict.NotEvaluable);
        gap[7] = C(8, PhaseVerdict.NotEvaluable);
        ReportsPhaseReport.HeadlineOf(gap).ShouldBe("P8 exit: NOT EVALUABLE (2 of 7 need evidence)");
        var both = All(PhaseVerdict.Met);
        both[0] = C(1, PhaseVerdict.NotEvaluable);
        both[1] = C(2, PhaseVerdict.NotMet);
        ReportsPhaseReport.HeadlineOf(both).ShouldBe("P8 exit: NOT MET");
    }

    [Fact]
    public void A_malformed_report_is_never_met()
    {
        ReportsPhaseReport.HeadlineOf(All(PhaseVerdict.Met).Where(c => c.Number != 7).ToList()).ShouldBe("P8 exit: NOT EVALUABLE, malformed report");
        var duplicate = All(PhaseVerdict.Met);
        duplicate.Add(C(3, PhaseVerdict.Met));
        ReportsPhaseReport.HeadlineOf(duplicate).ShouldBe("P8 exit: NOT EVALUABLE, malformed report");
        ReportsPhaseReport.HeadlineOf([]).ShouldBe("P8 exit: NOT EVALUABLE, malformed report");
    }

    [Fact]
    public void The_server_alone_can_never_print_met_and_always_prints_the_limits()
    {
        var report = ReportsPhaseCheck.Evaluate(Facts(stopwatch: Good));
        report.Headline().ShouldStartWith("P8 exit: NOT EVALUABLE");
        report.Limits.ShouldNotBeEmpty();
        report.ToText().ShouldContain("limit:");
        report.ToText().ShouldBe(ReportsPhaseCheck.Evaluate(Facts(stopwatch: Good)).ToText());
    }

    [Fact]
    public void The_approved_write_list_is_the_start_and_the_cancel_of_the_replay_job_and_nothing_else()
    {
        ApprovedReportWrites.All.ShouldBe(["POST /api/experiments/", "POST /api/experiments/jobs/{id:guid}/cancel"]);
    }
}
