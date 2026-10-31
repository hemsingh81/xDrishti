using XDrishti.Domain.Execution.PhaseCheck;
using static XDrishti.Domain.Tests.Execution.PhaseCheck.PhaseBuilders;

namespace XDrishti.Domain.Tests.Execution.PhaseCheck;

/// <summary>Spec P12-12 criteria 2 to 5, 8 to 12: streak, verdict, boundaries and the text.</summary>
public sealed class ExecutionPhaseCheckTests
{
    private static DateOnly Day(int n) => Start.AddDays(n - 1);

    private static List<SessionFacts> HandTable(bool d4Broken = true, bool d6Broken = true, int upTo = 10)
    {
        var kinds = new Func<DateOnly, SessionFacts>[]
        {
            Counted, Idle, Counted, d4Broken ? Broken : Counted,
            Counted, d6Broken ? d => Idle(d) with { PositionCarriedIn = true, DayState = "not_ready", Eod = new RunFact(true), Final = new RunFact(false) } : Counted,
            Counted, Counted, Idle, Counted,
        };
        return [.. kinds.Take(upTo).Select((make, i) => make(Day(i + 1)))];
    }

    [Fact]
    public void The_hand_table_is_met_at_D10_with_the_exact_label()
    {
        var report = Evaluate(HandTable(), 3);

        var account = report.Accounts.Single();
        account.Verdict.ShouldBe(PhaseVerdicts.Met);
        account.Streak.ShouldBe(3);
        account.Label.ShouldBe($"MET (3 counted sessions since {Day(7):yyyy-MM-dd}; 2 broken sessions earlier in the window, {Day(4):yyyy-MM-dd} and {Day(6):yyyy-MM-dd}; 2 idle sessions)");
        account.FirstMetAt.ShouldBe(Day(10));
        report.ExitVerdict.ShouldBe(PhaseVerdicts.Met);
    }

    [Fact]
    public void The_hand_table_at_D9_is_not_met_because_a_broken_session_is_in_the_window()
    {
        var account = Evaluate(HandTable(upTo: 9), 3).Accounts.Single();

        account.Verdict.ShouldBe(PhaseVerdicts.NotMet);
        account.Streak.ShouldBe(2);
        account.Label.ShouldContain("streak 2 of 3");
        account.LastBreak!.ShouldContain(Day(6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Two_counted_sessions_with_nothing_against_them_are_not_evaluable_two_of_three()
    {
        var account = Evaluate([Counted(Day(1)), Idle(Day(2)), Counted(Day(3))], 3).Accounts.Single();

        account.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        account.Label.ShouldContain("2 of 3");
    }

    [Fact]
    public void Twenty_nine_counted_sessions_is_not_evaluable_one_more_needed()
    {
        var account = Evaluate(Run(29, Counted)).Accounts.Single();

        account.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        account.Label.ShouldContain("29 of 30, 1 more needed");
    }

    [Fact]
    public void Exactly_thirty_counted_sessions_is_met()
    {
        var account = Evaluate(Run(30, Counted)).Accounts.Single();

        account.Verdict.ShouldBe(PhaseVerdicts.Met);
        account.Streak.ShouldBe(30);
    }

    [Fact]
    public void A_break_in_session_31_turns_a_later_check_to_not_met_and_the_first_met_date_stays()
    {
        var sessions = Run(30, Counted);
        sessions.Add(Broken(Start.AddDays(30)));

        var account = Evaluate(sessions).Accounts.Single();

        account.Verdict.ShouldBe(PhaseVerdicts.NotMet);
        account.FirstMetAt.ShouldBe(Start.AddDays(29));
    }

    [Fact]
    public void A_break_in_the_streak_resets_it_to_zero()
    {
        var sessions = Run(20, Counted);
        sessions.Add(Broken(Start.AddDays(20)));
        sessions.AddRange(Enumerable.Range(21, 29).Select(i => Counted(Start.AddDays(i))));

        var account = Evaluate(sessions).Accounts.Single();

        account.Streak.ShouldBe(29);
        account.Verdict.ShouldBe(PhaseVerdicts.NotMet);
    }

    [Fact]
    public void Idle_and_out_of_mode_sessions_leave_the_streak()
    {
        var sessions = Run(10, Counted);
        sessions.Add(Idle(Start.AddDays(10)));
        sessions.Add(Out(Start.AddDays(11)));

        Evaluate(sessions).Accounts.Single().Streak.ShouldBe(10);
    }

    [Fact]
    public void Empty_window_and_no_semi_auto_window_are_not_evaluable_with_the_limits_printed()
    {
        var report = ExecutionPhaseCheck.Evaluate(new ExecutionPhaseFacts(Start, [Account([], "manual_live", null)], CleanBoundary));

        report.ExitVerdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Accounts.Single().Label.ShouldContain("no semi-auto window");
        report.Limits.ShouldNotBeEmpty();
        ExecutionPhaseText.Render(report).ShouldContain("honest limits:");
    }

    [Fact]
    public void No_account_at_all_is_not_evaluable()
    {
        var report = ExecutionPhaseCheck.Evaluate(new ExecutionPhaseFacts(Start, [], CleanBoundary));

        report.ExitVerdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.ExitCriterion.ShouldContain("no account");
    }

    [Fact]
    public void An_account_that_is_no_longer_in_semi_auto_is_never_met()
    {
        var account = Evaluate(Run(30, Counted), mode: "assisted").Accounts.Single();

        account.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
    }

    [Fact]
    public void Sandbox_only_history_reads_not_evaluable_and_says_how_many_sandbox_sessions_are_not_counted()
    {
        var sessions = Run(4, d => Idle(d) with { SandboxIntents = 2 });

        var account = Evaluate(sessions).Accounts.Single();

        account.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        account.Label.ShouldContain("sandbox sessions are not counted: 4");
        account.Counted.ShouldBe(0);
    }

    [Fact]
    public void A_malformed_window_with_a_duplicate_date_is_never_met()
    {
        var sessions = Run(30, Counted);
        sessions[10] = sessions[9];

        var account = Evaluate(sessions).Accounts.Single();

        account.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        account.Label.ShouldContain("malformed window");
    }

    [Fact]
    public void The_exit_is_met_when_one_account_is_met_even_if_another_is_not_met()
    {
        var met = Account(Run(30, Counted));
        var notMet = Account(Run(5, Broken)) with { AccountId = Guid.NewGuid(), Name = "Other" };

        var report = ExecutionPhaseCheck.Evaluate(new ExecutionPhaseFacts(Start.AddDays(29), [met, notMet], CleanBoundary));

        report.ExitVerdict.ShouldBe(PhaseVerdicts.Met);
        report.Accounts.Select(a => a.Verdict).ShouldBe([PhaseVerdicts.Met, PhaseVerdicts.NotMet]);
    }

    [Fact]
    public void The_session_line_carries_runs_breaks_and_the_insufficient_median()
    {
        var line = Evaluate([Broken(Start) with { Final = null }], 3).Accounts.Single().Sessions.Single();

        line.Class.ShouldBe("broken");
        line.FinalRun.ShouldBe("missing");
        line.EodRun.ShouldBe("readable");
        line.BreakKinds.ShouldBe(["position_mismatch"]);
        line.MedianState.ShouldBe("insufficient");
    }

    [Fact]
    public void The_text_is_identical_on_a_second_run_and_keeps_the_exit_word_bare_beside_the_labelled_criterion()
    {
        var a = ExecutionPhaseText.Render(Evaluate(HandTable(), 3));
        var b = ExecutionPhaseText.Render(Evaluate(HandTable(), 3));

        a.ShouldBe(b);
        a.ShouldContain("exit verdict: MET\n");
        a.ShouldContain("exit criterion: 3 semi-auto sessions with zero reconciliation breaks: MET");
    }

    [Fact]
    public void A_boundary_that_differs_from_its_pin_is_printed_not_met_and_does_not_change_the_streak()
    {
        var facts = new ExecutionPhaseFacts(Start.AddDays(29), [Account(Run(30, Counted))], new BoundaryFacts(["a", "POST orders"], ["a"], ["POST https://api.dhan.co/v2/orders"]), 30);

        var report = ExecutionPhaseCheck.Evaluate(facts);

        report.Boundary.Text.ShouldStartWith("NOT MET");
        report.Accounts.Single().Verdict.ShouldBe(PhaseVerdicts.Met);
    }

    [Fact]
    public void Facts_with_no_source_are_unavailable_and_slippage_is_insufficient_below_thirty_fills()
    {
        var account = ExecutionPhaseCheck.Evaluate(new ExecutionPhaseFacts(Start, [Account([Counted(Start)]) with { Extras = AccountExtras.Unread with { LiveLegs = 2, LiveLegsWithAlgoId = 1, LiveEntryFills = 5 } }], CleanBoundary)).Accounts.Single();

        account.Facts.Single(f => f.Name == "protection_coverage").State.ShouldBe("unavailable");
        account.Facts.Single(f => f.Name == "slippage").State.ShouldBe("insufficient");
        account.Facts.Single(f => f.Name == "algo_id").Text.ShouldBe("1 of 2 live legs");
        account.Facts.Single(f => f.Name == "kill_switch_drills").State.ShouldBe("empty");
    }

    [Fact]
    public void Over_two_thousand_random_lists_the_streak_matches_a_rescan_and_met_needs_enough_counted_sessions_and_an_added_break_never_helps()
    {
        var rng = new Random(12);
        for (var seed = 0; seed < 2000; seed++)
        {
            var sessions = Enumerable.Range(0, rng.Next(0, 70)).Select(i => Make(Start.AddDays(i), rng.Next(4))).ToList();
            var report = Evaluate(sessions, 5).Accounts.SingleOrDefault();
            var reference = Reference(sessions);

            if (sessions.Count == 0)
            {
                continue;
            }

            report!.Streak.ShouldBe(reference);
            if (report.Verdict == PhaseVerdicts.Met)
            {
                report.Counted.ShouldBeGreaterThanOrEqualTo(5);
            }

            var worse = Evaluate([.. sessions, Broken(Start.AddDays(sessions.Count))], 5).Accounts.Single();
            (report.Verdict != PhaseVerdicts.Met && worse.Verdict == PhaseVerdicts.Met).ShouldBeFalse();
            var withIdle = Evaluate([.. sessions, Idle(Start.AddDays(sessions.Count))], 5).Accounts.Single();
            withIdle.Verdict.ShouldBe(report.Verdict);
        }
    }

    private static SessionFacts Make(DateOnly d, int kind) => kind switch { 0 => Counted(d), 1 => Idle(d), 2 => Broken(d), _ => Out(d) };

    private static int Reference(List<SessionFacts> sessions)
    {
        var streak = 0;
        foreach (var s in sessions)
        {
            var c = SessionClassifier.Classify(s).Class;
            streak = c == SessionClass.Counted ? streak + 1 : c == SessionClass.Broken ? 0 : streak;
        }

        return streak;
    }
}
