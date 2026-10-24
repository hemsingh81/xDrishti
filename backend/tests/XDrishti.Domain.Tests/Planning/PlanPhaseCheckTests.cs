using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>
/// Spec P7-18: the P7 phase check by hand tables. The exit (criterion 1) is MET only for ten consecutive trading sessions, each with a published plan from a scheduled run and no failed attempt; a manual run, a failed-closed attempt or a gap breaks the run;
/// too little history is NOT EVALUABLE; the tests-only criteria are never judged; the limits and the blockers are always printed.
/// </summary>
public sealed class PlanPhaseCheckTests
{
    // Ten consecutive weekdays (a Friday to Friday with the weekend skipped by the caller: the check takes the window as given).
    private static readonly DateOnly[] Dates = [.. Enumerable.Range(0, 14).Select(i => new DateOnly(2026, 10, 5).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))];

    private static readonly DateTimeOffset At = new(2026, 10, 9, 13, 0, 0, TimeSpan.Zero);

    private static PhaseRun Published(long id, bool manual = false, long planId = 1) =>
        new(id, 1, "published", null, "Published", manual, At, At.AddMinutes(1), planId);

    private static PhaseRun Failed(long id, string gate = "eod", bool manual = false) =>
        new(id, 1, "failed_closed", gate, "the end-of-day run had not finished", manual, At, At.AddMinutes(1), null);

    private static PhaseSessionInput Good(DateOnly date, int primary = 2, string basis = "cell statistics only") =>
        new(date, [Published(date.DayNumber, planId: date.DayNumber)], new PhasePlan(date.DayNumber, basis, primary, 1, 3));

    private static List<PhaseSessionInput> Ten() => [.. Dates.Take(10).Select(d => Good(d))];

    private static PlanPhaseFacts Facts(
        List<PhaseSessionInput>? window = null,
        DateOnly? first = null,
        bool noHistory = false,
        PhaseFailClosedFacts? failClosed = null,
        PhaseAllowListFacts? allowList = null,
        PhaseEvidenceFacts? evidence = null,
        PhaseReviewFacts? review = null,
        PhaseAllocationFacts? allocation = null,
        PhaseGradeFacts? grades = null,
        PhaseJournalFacts? journal = null,
        PhaseSetupFacts? setup = null)
    {
        window ??= Ten();
        return new PlanPhaseFacts(
            window[^1].Date,
            window,
            noHistory ? null : first ?? window[0].Date,
            failClosed ?? new PhaseFailClosedFacts(10, false, [], 0),
            allowList ?? new PhaseAllowListFacts(PlanPhaseCheck.PinnedAllowList, PlanPhaseCheck.PinnedAllowList, []),
            evidence ?? new PhaseEvidenceFacts(10, 20, 0, 0, 0),
            review ?? new PhaseReviewFacts(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false),
            allocation ?? new PhaseAllocationFacts(0, 0),
            grades ?? new PhaseGradeFacts(0, 0, 0, 0),
            journal ?? new PhaseJournalFacts(0, 0, 0, 0),
            setup ?? new PhaseSetupFacts([], [], []));
    }

    private static PlanPhaseCriterion Criterion(PlanPhaseReport report, int number) => report.Criteria.Single(c => c.Number == number);

    [Fact]
    public void Ten_consecutive_scheduled_published_sessions_meet_the_exit()
    {
        var report = PlanPhaseCheck.Evaluate(Facts());

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.Met);
        report.Streak.ShouldBe(10);
        report.Sessions.ShouldAllBe(s => s.State == "counted" && s.ManualRuns == 0 && s.FailedRuns == 0 && s.Published);
        Criterion(report, 1).Caveat.ShouldBeNull();
        report.ToText().ShouldContain("1. MET  Plans published 10 consecutive trading sessions unattended");
    }

    [Fact]
    public void Ten_empty_skeleton_plans_meet_the_count_but_the_label_says_so()
    {
        var window = Dates.Take(10).Select(d => Good(d, 0, "skeleton: no builder")).ToList();

        var report = PlanPhaseCheck.Evaluate(Facts(window));

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.Met);
        Criterion(report, 1).Caveat.ShouldBe("10 of 10 plans have zero primary tickets, 10 of them skeleton");
        report.Sessions.ShouldAllBe(s => s.ZeroPrimary && s.Skeleton);
        report.ToText().ShouldContain("MET (10 of 10 plans have zero primary tickets, 10 of them skeleton)");
    }

    [Fact]
    public void A_manual_run_in_the_window_breaks_the_run()
    {
        var window = Ten();
        window[4] = window[4] with { Runs = [Published(window[4].Date.DayNumber, manual: true)] };

        var report = PlanPhaseCheck.Evaluate(Facts(window));

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
        report.Sessions[4].State.ShouldBe("manual_run");
        report.Sessions[4].ManualRuns.ShouldBe(1);
        report.Streak.ShouldBe(5);
        Criterion(report, 1).Detail.ShouldContain("manual runs in the window 1");
    }

    [Fact]
    public void A_manual_retry_after_a_scheduled_failure_is_not_unattended_either()
    {
        var window = Ten();
        window[9] = window[9] with { Runs = [Failed(1), Published(2, manual: true)] };

        var report = PlanPhaseCheck.Evaluate(Facts(window));

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
        report.Sessions[9].State.ShouldBe("manual_run");
        (report.Sessions[9].ScheduledRuns, report.Sessions[9].ManualRuns, report.Sessions[9].FailedRuns).ShouldBe((1, 1, 1));
    }

    [Fact]
    public void A_failed_closed_day_breaks_the_run_with_its_gate_and_reason_even_if_a_scheduled_retry_published()
    {
        var window = Ten();
        window[2] = window[2] with { Runs = [Failed(1, "eod"), Published(2)] };
        window[7] = window[7] with { Runs = [Failed(3, "data-quality")], Plan = null };

        var report = PlanPhaseCheck.Evaluate(Facts(window));

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
        report.Sessions[2].State.ShouldBe("failed_closed");
        report.Sessions[7].State.ShouldBe("failed_closed");
        report.Sessions[7].Note.ShouldContain("data-quality: the end-of-day run had not finished");
        report.Sessions[7].Gates.ShouldBe("4 pass, data-quality did not pass");
        report.Streak.ShouldBe(2);
    }

    [Fact]
    public void A_day_with_no_run_after_the_history_began_breaks_the_run()
    {
        var window = Ten();
        window[5] = window[5] with { Runs = [], Plan = null };

        var report = PlanPhaseCheck.Evaluate(Facts(window));

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
        report.Sessions[5].State.ShouldBe("no_run");
        Criterion(report, 1).Detail.ShouldContain($"{window[5].Date:yyyy-MM-dd} no_run");
    }

    [Fact]
    public void A_published_row_whose_plan_is_not_stored_breaks_the_run()
    {
        var window = Ten();
        window[9] = window[9] with { Plan = null };

        var report = PlanPhaseCheck.Evaluate(Facts(window));

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
        report.Sessions[9].State.ShouldBe("plan_missing");
    }

    [Fact]
    public void A_gap_in_the_dates_for_a_weekend_or_a_holiday_is_not_a_gap()
    {
        // The window lists trading sessions only (the caller walks the calendar), so Friday then Monday, or a holiday between, are neighbours.
        var window = Ten();

        var report = PlanPhaseCheck.Evaluate(Facts(window));

        window.Zip(window.Skip(1)).ShouldContain(p => p.Second.Date.DayNumber - p.First.Date.DayNumber > 1);
        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.Met);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Fewer_than_ten_sessions_of_history_is_not_evaluable_and_never_met(int recorded)
    {
        var window = Ten();
        for (var i = 0; i < 10 - recorded; i++)
        {
            window[i] = window[i] with { Runs = [], Plan = null };
        }

        var report = PlanPhaseCheck.Evaluate(Facts(window, first: recorded == 0 ? null : window[10 - recorded].Date, noHistory: recorded == 0));

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        report.Streak.ShouldBe(recorded);
        report.Sessions.Count(s => s.State == "before_history").ShouldBe(10 - recorded);
        Criterion(report, 1).Detail.ShouldContain($"{10 - recorded} more trading session(s)");
    }

    [Fact]
    public void A_fact_against_the_run_wins_over_missing_history()
    {
        var window = Ten();
        window[0] = window[0] with { Runs = [], Plan = null };
        window[8] = window[8] with { Runs = [Failed(9)], Plan = null };

        var report = PlanPhaseCheck.Evaluate(Facts(window, first: window[1].Date));

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
    }

    [Fact]
    public void A_window_shorter_than_ten_sessions_is_never_met()
    {
        var report = PlanPhaseCheck.Evaluate(Facts(Ten().Take(9).ToList()));

        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
    }

    [Fact]
    public void More_rows_than_were_read_mean_the_history_is_long_so_an_empty_day_is_a_gap_not_missing_history()
    {
        var window = Ten();
        window[0] = window[0] with { Runs = [], Plan = null };

        var report = PlanPhaseCheck.Evaluate(Facts(window, first: DateOnly.MinValue));

        report.Sessions[0].State.ShouldBe("no_run");
        report.ExitCriterion.ShouldBe(PlanPhaseVerdicts.NotMet);
    }

    [Fact]
    public void The_finish_time_and_the_plan_counts_are_printed_for_every_session()
    {
        var text = PlanPhaseCheck.Evaluate(Facts()).ToText();

        text.ShouldContain("2026-10-05  counted  scheduled 1 manual 0 failed 0  published  plan ");
        text.ShouldContain("primary 2 reserve 1 rejected 3");
        text.ShouldContain("gates: 6/6 pass  finished 2026-10-09 18:31 IST");
    }

    [Fact]
    public void The_exit_is_the_verdict_of_criterion_one_and_the_summary_counts_add_up()
    {
        var report = PlanPhaseCheck.Evaluate(Facts());

        report.ExitCriterion.ShouldBe(Criterion(report, 1).Verdict);
        report.Criteria.Select(c => c.Number).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        report.ToText().ShouldContain($"summary: {report.Criteria.Count(c => c.Verdict == "MET")} met, {report.Criteria.Count(c => c.Verdict == "NOT MET")} not met, {report.Criteria.Count(c => c.Verdict == "NOT EVALUABLE")} not evaluable");
    }

    // Criterion 2: fail closed.
    [Fact]
    public void Fail_closed_has_its_three_outcomes()
    {
        var none = Criterion(PlanPhaseCheck.Evaluate(Facts()), 2);
        var some = Criterion(PlanPhaseCheck.Evaluate(Facts(failClosed: new PhaseFailClosedFacts(12, false, [new PhaseGateCount("eod", 2), new PhaseGateCount("data-quality", 1)], 0))), 2);
        var all = Criterion(PlanPhaseCheck.Evaluate(Facts(failClosed: new PhaseFailClosedFacts(12, false, [.. PlanPhaseCheck.GateOrder.Select(g => new PhaseGateCount(g, 1))], 0))), 2);
        var bad = Criterion(PlanPhaseCheck.Evaluate(Facts(failClosed: new PhaseFailClosedFacts(12, false, [new PhaseGateCount("eod", 2)], 1))), 2);

        none.Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        some.Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        some.Detail.ShouldContain("3 failed-closed row(s)");
        some.Detail.ShouldContain("eod 2, data-quality 1");
        some.Detail.ShouldContain("calendar, portfolio-sync, fresh-data, selection-inputs has not been seen failing");
        all.Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        all.Caveat.ShouldNotBeNull();
        bad.Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
    }

    // Criterion 4: no order.
    [Fact]
    public void The_allow_list_has_its_three_outcomes()
    {
        var same = Criterion(PlanPhaseCheck.Evaluate(Facts()), 4);
        var added = Criterion(PlanPhaseCheck.Evaluate(Facts(allowList: new PhaseAllowListFacts([.. PlanPhaseCheck.PinnedAllowList, "POST api.dhan.co/v2/orders"], PlanPhaseCheck.PinnedAllowList, []))), 4);
        var removed = Criterion(PlanPhaseCheck.Evaluate(Facts(allowList: new PhaseAllowListFacts(PlanPhaseCheck.PinnedAllowList.Skip(1).ToList(), PlanPhaseCheck.PinnedAllowList, []))), 4);
        var probe = Criterion(PlanPhaseCheck.Evaluate(Facts(allowList: new PhaseAllowListFacts(PlanPhaseCheck.PinnedAllowList, PlanPhaseCheck.PinnedAllowList, ["POST https://api.dhan.co/v2/orders"]))), 4);
        var unreadable = Criterion(PlanPhaseCheck.Evaluate(Facts(allowList: new PhaseAllowListFacts([], PlanPhaseCheck.PinnedAllowList, []))), 4);

        same.Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        same.Caveat!.ShouldContain("not run here");
        added.Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        added.Detail.ShouldContain("POST api.dhan.co/v2/orders");
        removed.Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        probe.Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        unreadable.Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
    }

    [Fact]
    public void The_pinned_allow_list_has_no_order_position_or_fund_endpoint()
    {
        PlanPhaseCheck.PinnedAllowList.ShouldNotContain(e => e.Contains("order", StringComparison.OrdinalIgnoreCase) || e.Contains("position", StringComparison.OrdinalIgnoreCase) || e.Contains("fund", StringComparison.OrdinalIgnoreCase));
        PlanPhaseCheck.RefusedProbes.Count.ShouldBeGreaterThan(5);
    }

    // Criterion 6: evidence.
    [Fact]
    public void Evidence_has_its_three_outcomes()
    {
        Criterion(PlanPhaseCheck.Evaluate(Facts(evidence: new PhaseEvidenceFacts(0, 0, 0, 0, 0))), 6).Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        Criterion(PlanPhaseCheck.Evaluate(Facts(evidence: new PhaseEvidenceFacts(10, 0, 0, 0, 0))), 6).Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        Criterion(PlanPhaseCheck.Evaluate(Facts(evidence: new PhaseEvidenceFacts(10, 12, 0, 0, 0))), 6).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        Criterion(PlanPhaseCheck.Evaluate(Facts(evidence: new PhaseEvidenceFacts(10, 12, 1, 0, 0))), 6).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        Criterion(PlanPhaseCheck.Evaluate(Facts(evidence: new PhaseEvidenceFacts(10, 12, 0, 0, 2))), 6).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
    }

    // Criterion 7: review.
    [Fact]
    public void The_review_workflow_has_its_three_outcomes()
    {
        var nothing = Criterion(PlanPhaseCheck.Evaluate(Facts()), 7);
        var owner = Criterion(PlanPhaseCheck.Evaluate(Facts(review: new PhaseReviewFacts(10, 1, 3, 2, 1, 1, 0, 4, 0, 0, 0, false))), 7);
        var lapsedOnly = Criterion(PlanPhaseCheck.Evaluate(Facts(review: new PhaseReviewFacts(10, 3, 0, 0, 0, 0, 0, 5, 0, 0, 0, false))), 7);

        nothing.Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        owner.Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        owner.Detail.ShouldContain("auto approvals are 0");
        lapsedOnly.Verdict.ShouldBe(PlanPhaseVerdicts.Met);
    }

    [Theory]
    [InlineData(1, 0, 0, 0, false, "auto approval(s) exist although the switch was never stored as ON")]
    [InlineData(0, 1, 0, 0, false, "decision(s) dated at or after the cutoff")]
    [InlineData(0, 0, 1, 0, false, "approved or armed ticket(s) without an approve row")]
    [InlineData(0, 0, 0, 2, false, "ticket(s) still undecided well after the cutoff")]
    public void A_fact_against_the_review_workflow_is_not_met(int auto, int afterCutoff, int noRow, int undecided, bool switchOn, string expected)
    {
        var criterion = Criterion(PlanPhaseCheck.Evaluate(Facts(review: new PhaseReviewFacts(10, 0, 1, 1, 0, 0, auto, 0, undecided, afterCutoff, noRow, switchOn))), 7);

        criterion.Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
        criterion.Detail.ShouldContain(expected);
    }

    [Fact]
    public void Auto_approvals_under_a_switch_the_owner_turned_on_are_counted_not_flagged()
    {
        var criterion = Criterion(PlanPhaseCheck.Evaluate(Facts(review: new PhaseReviewFacts(10, 0, 1, 0, 0, 0, 2, 0, 0, 0, 0, true))), 7);

        criterion.Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        criterion.Detail.ShouldContain("auto approved 2");
        criterion.Detail.ShouldContain("under a switch the owner turned ON");
    }

    // Criteria 8, 9, 11.
    [Fact]
    public void Accounts_grades_and_journal_have_their_outcomes()
    {
        Criterion(PlanPhaseCheck.Evaluate(Facts()), 8).Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        Criterion(PlanPhaseCheck.Evaluate(Facts(allocation: new PhaseAllocationFacts(6, 0))), 8).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        Criterion(PlanPhaseCheck.Evaluate(Facts(allocation: new PhaseAllocationFacts(6, 1))), 8).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);

        Criterion(PlanPhaseCheck.Evaluate(Facts()), 9).Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        Criterion(PlanPhaseCheck.Evaluate(Facts(grades: new PhaseGradeFacts(3, 1, 8, 10))), 9).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        Criterion(PlanPhaseCheck.Evaluate(Facts(grades: new PhaseGradeFacts(3, 0, 11, 10))), 9).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);

        Criterion(PlanPhaseCheck.Evaluate(Facts()), 11).Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        Criterion(PlanPhaseCheck.Evaluate(Facts(journal: new PhaseJournalFacts(2, 4, 1, 0))), 11).Verdict.ShouldBe(PlanPhaseVerdicts.Met);
        Criterion(PlanPhaseCheck.Evaluate(Facts(journal: new PhaseJournalFacts(2, 4, 1, 1))), 11).Verdict.ShouldBe(PlanPhaseVerdicts.NotMet);
    }

    [Fact]
    public void The_criteria_that_tests_prove_are_never_judged_from_records()
    {
        var report = PlanPhaseCheck.Evaluate(Facts(evidence: new PhaseEvidenceFacts(10, 12, 0, 0, 0), review: new PhaseReviewFacts(10, 0, 1, 2, 0, 0, 0, 0, 0, 0, 0, false)));

        foreach (var number in new[] { 3, 5, 10, 12 })
        {
            Criterion(report, number).Verdict.ShouldBe(PlanPhaseVerdicts.NotEvaluable);
        }

        Criterion(report, 3).Detail.ShouldContain("runs no test");
    }

    // Always printed.
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void The_limits_are_printed_whatever_the_outcome(int recorded)
    {
        var window = Ten();
        for (var i = 0; i < 10 - recorded; i++)
        {
            window[i] = window[i] with { Runs = [], Plan = null };
        }

        var report = PlanPhaseCheck.Evaluate(Facts(window, first: recorded == 0 ? null : window[10 - recorded].Date, noHistory: recorded == 0));
        var text = report.ToText();

        report.Limits.Count.ShouldBeGreaterThan(5);
        foreach (var limit in report.Limits)
        {
            text.ShouldContain("limit: " + limit);
        }

        text.ShouldContain("limit: about 2.1 years of data");
        text.ShouldContain("no cell is Active before P10");
        text.ShouldContain("the empty-plan risk");
        text.ShouldContain("have no data source");
        text.ShouldContain("pre-open quote format is unverified");
        text.ShouldContain("nothing is written");
    }

    [Fact]
    public void The_blockers_name_the_services_and_switches_that_are_off_and_why()
    {
        var setup = new PhaseSetupFacts(
            [new PhaseService("nightly", "the nightly plan", true, "paused"), new PhaseService("preopen", "the pre-open check and arming", false, "enabled"), new PhaseService("weekly", "the weekly learning digest", false, "paused")],
            [new PhaseSwitch("Planning:Builder:UseSelection", false, "the selection builder; OFF means only the skeleton plan", true), new PhaseSwitch("autonomy.auto_approve_tickets", false, "optional", false)],
            [new PhaseCount("plans read", 0)]);

        var report = PlanPhaseCheck.Evaluate(Facts(setup: setup));
        var text = report.ToText();

        report.Blockers.Count.ShouldBe(3);
        text.ShouldContain("blocker: service nightly is paused: it is needed for the exit (the nightly plan), so no plan is published by schedule until the owner enables it");
        text.ShouldContain("blocker: service weekly is paused: the weekly learning digest does not happen until the owner enables it");
        text.ShouldContain("blocker: switch Planning:Builder:UseSelection is OFF");
        text.ShouldNotContain("blocker: switch autonomy.auto_approve_tickets");
        text.ShouldContain("switch autonomy.auto_approve_tickets: OFF");
        text.ShouldContain("service preopen: enabled");
        text.ShouldContain("count plans read: 0");
    }

    [Fact]
    public void The_same_facts_give_the_same_text()
    {
        var a = PlanPhaseCheck.Evaluate(Facts()).ToText();
        var b = PlanPhaseCheck.Evaluate(Facts()).ToText();

        b.ShouldBe(a);
    }

    [Fact]
    public void A_reason_with_control_characters_is_printed_on_one_line()
    {
        var window = Ten();
        window[9] = window[9] with { Runs = [new PhaseRun(1, 1, "failed_closed", "eod", "line one\nline two\u001b[31m", false, At, At, null)], Plan = null };

        var text = PlanPhaseCheck.Evaluate(Facts(window)).ToText();

        text.ShouldContain("eod: line one line two [31m");
        text.ShouldNotContain("\u001b");
    }
}
