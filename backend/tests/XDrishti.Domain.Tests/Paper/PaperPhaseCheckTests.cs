using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>Spec P10-06: the hand table (epoch starting Monday 2026-11-02; day 85 is Monday 2027-01-25, day 71 is Monday 2027-01-11), the label stacking, the malformed window and the epochs that are listed and never pooled.</summary>
public sealed class PaperPhaseCheckTests
{
    private static readonly DateOnly Start = new(2026, 11, 2);

    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(5.5));

    private static readonly CriterionResult Met = new("met", "paper mean +0.100R is above 0");

    private static readonly CriterionResult DrawdownMet = new("met", "paper largest fall 2.000R, backtest 90th percentile at 61 trades 3.000R: within");

    private static List<PhaseSessionFact> Sessions(DateOnly through, int covered, int skipHolidays = 1, int manual = 0, int integrity = 0)
    {
        var list = new List<PhaseSessionFact>();
        var holiday = new DateOnly(2026, 12, 25);
        for (var d = Start; d <= through; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || (skipHolidays > 0 && d == holiday))
            {
                continue;
            }

            var ok = list.Count < covered;
            list.Add(new PhaseSessionFact(d, ok, 0, list.Count == 0 ? manual : 0, 2, 1, ok, false, list.Count == 1 ? integrity : 0));
        }

        return list;
    }

    private static List<CountedTrade> Trades(int n, int auto = 0) =>
        [.. Enumerable.Range(1, n).Select(i => new CountedTrade("cell", 0.1, "net", "flat-10", T0.AddDays(i), T0.AddDays(i).AddHours(2), i, i <= auto ? "auto" : "owner"))];

    private static PaperPhaseFacts Facts(
        DateOnly? through = null, int covered = 50, int n = 61, int auto = 0, int risk = 2, int integrity = 0, CriterionResult? e3 = null, CriterionResult? e4 = null, bool confirmed = true, string p7 = "MET", bool isSession = true, int manual = 0, double lo = -0.12)
    {
        var end = through ?? new DateOnly(2027, 1, 25);
        var epoch = new PhaseEpochFact(2, Start, "owner_restart", "start", n, 0, risk);
        return new PaperPhaseFacts(
            epoch,
            [new PhaseEpochFact(1, new DateOnly(2026, 9, 1), "first", "first", 61, 0, 0), epoch],
            end, isSession, Sessions(end, covered, manual: manual, integrity: integrity), Trades(n, auto),
            new PhaseExitFacts(2, e3 ?? Met, e4 ?? DrawdownMet, lo, 0.51, null, "n 70, mean +0.050 (all)", confirmed),
            [new PhaseChangeFact(new DateOnly(2026, 12, 1), "risk_down"), new PhaseChangeFact(new DateOnly(2026, 12, 8), "risk_down")],
            p7, "label", ["Followed: n 40"], [], []);
    }

    private static string Verdict(PaperPhaseReport r, string id) => r.Criteria.Single(c => c.Id == id).Verdict;

    [Fact]
    public void Day_85_with_coverage_083_and_61_trades_is_met_and_the_caveats_stack_in_the_label()
    {
        var report = PaperPhaseCheck.Evaluate(Facts());

        report.ElapsedDays.ShouldBe(85);
        report.Criteria.Select(c => c.Verdict).ShouldBe(["MET", "MET", "MET", "MET", "MET"]);
        report.Criteria[0].Label.ShouldContain("coverage 0.83 (50 of 60 sessions)");
        report.ExitVerdict.ShouldBe("MET");
        report.ExitCriterion.ShouldBe("MET (point estimate; 90% interval [-0.12, +0.51] includes 0; 2 risk-down changes: the population was censored)");
    }

    [Fact]
    public void Every_caveat_can_stack()
    {
        var report = PaperPhaseCheck.Evaluate(Facts(auto: 3, manual: 2, p7: "NOT MET"));

        report.ExitCriterion.ShouldBe("MET (point estimate; 90% interval [-0.12, +0.51] includes 0; 2 risk-down changes: the population was censored; 3 of 61 counted trades were auto-approved; 2 manual runs of the nightly pipeline in the epoch; P7 phase check not met)");
    }

    [Fact]
    public void An_interval_above_zero_and_no_other_caveat_is_a_bare_met()
    {
        var report = PaperPhaseCheck.Evaluate(Facts(risk: 0, lo: 0.05));

        report.ExitCriterion.ShouldBe("MET");
    }

    [Fact]
    public void Fewer_than_60_counted_trades_is_not_evaluable_and_never_met()
    {
        var report = PaperPhaseCheck.Evaluate(Facts(n: 58));

        Verdict(report, "C2").ShouldBe("NOT EVALUABLE");
        report.Criteria.Single(c => c.Id == "C2").Label.ShouldBe("NOT EVALUABLE: 58 of 60");
        report.ExitVerdict.ShouldBe("NOT EVALUABLE");
        report.ExitCriterion.ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void Day_71_is_not_evaluable_for_duration_whatever_the_count()
    {
        var report = PaperPhaseCheck.Evaluate(Facts(through: new DateOnly(2027, 1, 11), covered: 40, n: 66));

        report.ElapsedDays.ShouldBe(71);
        Verdict(report, "C1").ShouldBe("NOT EVALUABLE");
        report.Criteria[0].Label.ShouldBe("NOT EVALUABLE: day 71 of 84 (week 11 of 12)");
        report.ExitVerdict.ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void Day_84_is_still_short_by_the_boundary_rule_and_day_85_is_the_first_met_day()
    {
        var sunday = PaperPhaseCheck.Evaluate(Facts(through: new DateOnly(2027, 1, 24), covered: 50));
        var monday = PaperPhaseCheck.Evaluate(Facts());

        sunday.ElapsedDays.ShouldBe(84);
        Verdict(sunday, "C1").ShouldBe("MET");
        monday.ElapsedDays.ShouldBe(85);
    }

    [Fact]
    public void Coverage_072_at_day_90_is_not_met_and_lists_the_missing_sessions()
    {
        var report = PaperPhaseCheck.Evaluate(Facts(through: new DateOnly(2027, 1, 29), covered: 46));

        Verdict(report, "C1").ShouldBe("NOT MET");
        report.Criteria[0].Label.ShouldContain("coverage 0.72");
        report.Criteria[0].Facts.ShouldContain(f => f.StartsWith("missing sessions: ", StringComparison.Ordinal));
        report.ExitVerdict.ShouldBe("NOT MET");
        report.ExitCriterion.ShouldBe("NOT MET");
    }

    [Fact]
    public void A_non_positive_mean_is_not_met_for_expectancy()
    {
        var report = PaperPhaseCheck.Evaluate(Facts(e3: new CriterionResult("not_met", "paper mean -0.100R is not above 0 (n = 61)")));

        Verdict(report, "C3").ShouldBe("NOT MET");
        report.ExitVerdict.ShouldBe("NOT MET");
    }

    [Fact]
    public void An_unconfirmed_charge_sheet_leaves_expectancy_and_drawdown_not_evaluable_and_names_the_owner()
    {
        var gone = new CriterionResult("not_evaluable", "not evaluable: costs not confirmed");
        var report = PaperPhaseCheck.Evaluate(Facts(e3: gone, e4: gone, confirmed: false));

        report.Criteria.Single(c => c.Id == "C3").Label.ShouldBe("NOT EVALUABLE: costs not confirmed");
        report.Criteria.Single(c => c.Id == "C4").Verdict.ShouldBe("NOT EVALUABLE");
        report.Printed.Blockers.ShouldContain(b => b.Contains("owner's confirmation of the charge sheet", StringComparison.Ordinal));
        report.ExitVerdict.ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void A_comparison_that_says_met_without_a_confirmed_sheet_is_never_met()
    {
        var report = PaperPhaseCheck.Evaluate(Facts(confirmed: false));

        Verdict(report, "C3").ShouldBe("NOT EVALUABLE");
        Verdict(report, "C4").ShouldBe("NOT EVALUABLE");
        report.ExitVerdict.ShouldBe("NOT EVALUABLE");
        report.Criteria.Single(c => c.Id == "C3").Label.ShouldContain("costs not confirmed");
    }

    [Fact]
    public void A_met_with_a_missing_interval_end_is_labelled_and_never_bare()
    {
        var facts = Facts(risk: 0);

        var report = PaperPhaseCheck.Evaluate(facts with { Exit = facts.Exit! with { IntervalLow = null } });

        report.ExitCriterion.ShouldBe("MET (interval unavailable)");
    }

    [Fact]
    public void One_integrity_mismatch_is_not_met()
    {
        var report = PaperPhaseCheck.Evaluate(Facts(integrity: 1));

        Verdict(report, "I").ShouldBe("NOT MET");
        report.Criteria.Single(c => c.Id == "I").Label.ShouldContain("1 stored entries disagreed");
        report.ExitVerdict.ShouldBe("NOT MET");
    }

    [Fact]
    public void A_pair_pending_for_more_than_three_sessions_is_an_integrity_fact_but_a_recent_one_is_not()
    {
        var facts = Facts();
        var sessions = facts.Sessions.ToList();
        sessions[^2] = sessions[^2] with { CapturePending = true };
        sessions[10] = sessions[10] with { CapturePending = true };

        var recent = PaperPhaseCheck.Evaluate(facts with { Sessions = [.. sessions.Take(10).Concat(sessions.Skip(11))] });
        var stuck = PaperPhaseCheck.Evaluate(facts with { Sessions = sessions });

        Verdict(recent, "I").ShouldBe("MET");
        Verdict(stuck, "I").ShouldBe("NOT MET");
    }

    [Fact]
    public void No_capture_run_makes_integrity_not_evaluable()
    {
        var facts = Facts();

        var report = PaperPhaseCheck.Evaluate(facts with { Sessions = [.. facts.Sessions.Select(s => s with { CaptureRuns = 0, CaptureFinished = false })] });

        Verdict(report, "I").ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void An_epoch_restarted_yesterday_reads_day_one_and_the_earlier_epochs_trades_are_printed_not_counted()
    {
        var through = new DateOnly(2026, 11, 2);
        var facts = Facts(through: through, covered: 1, n: 0);

        var report = PaperPhaseCheck.Evaluate(facts);

        report.Criteria[0].Label.ShouldBe("NOT EVALUABLE: day 1 of 84 (week 1 of 12)");
        report.Criteria.Single(c => c.Id == "C2").Label.ShouldBe("NOT EVALUABLE: 0 of 60");
        report.Epochs.Select(e => (e.Id, e.Counted, e.Current)).ShouldBe([(1L, 61, false), (2L, 0, true)]);
    }

    [Fact]
    public void Earlier_epochs_are_never_pooled_into_the_count()
    {
        var report = PaperPhaseCheck.Evaluate(Facts(n: 5));

        report.Criteria.Single(c => c.Id == "C2").Label.ShouldBe("NOT EVALUABLE: 5 of 60");
        report.Epochs.Single(e => e.Id == 1).Counted.ShouldBe(61);
    }

    [Fact]
    public void A_window_that_does_not_hold_together_withholds_met()
    {
        var notSession = PaperPhaseCheck.Evaluate(Facts(isSession: false));
        var twice = Facts();
        var doubled = PaperPhaseCheck.Evaluate(twice with { Counted = [.. twice.Counted.Take(60), twice.Counted[0] with { R = 0.2 }] });
        var otherEpoch = Facts();
        var foreign = PaperPhaseCheck.Evaluate(otherEpoch with { Exit = otherEpoch.Exit! with { CompareEpochId = 9 } });

        notSession.ExitCriterion.ShouldBe("NOT EVALUABLE (malformed window)");
        doubled.ExitCriterion.ShouldBe("NOT EVALUABLE (malformed window)");
        foreign.ExitCriterion.ShouldBe("NOT EVALUABLE (malformed window)");
        notSession.Printed.Blockers.ShouldContain(b => b.StartsWith("malformed window", StringComparison.Ordinal));
    }

    [Fact]
    public void No_epoch_means_every_criterion_is_not_evaluable_with_the_reason()
    {
        var facts = Facts() with { Epoch = null, History = [], Sessions = [], Counted = [], Exit = null };

        var report = PaperPhaseCheck.Evaluate(facts);

        report.Epoch.ShouldBeNull();
        report.Criteria.ShouldAllBe(c => c.Verdict == "NOT EVALUABLE" && c.Label.Contains("no paper epoch", StringComparison.Ordinal));
        report.ExitVerdict.ShouldBe("NOT EVALUABLE");
        report.Limits.ShouldNotBeEmpty();
    }

    [Fact]
    public void An_unreadable_comparison_is_named_and_is_never_a_met()
    {
        var report = PaperPhaseCheck.Evaluate(Facts() with { Exit = null });

        Verdict(report, "C3").ShouldBe("NOT EVALUABLE");
        report.Criteria.Single(c => c.Id == "C3").Label.ShouldContain("could not be read");
        report.Printed.SensitivityMean.ShouldStartWith("unavailable");
    }

    [Fact]
    public void Week_lines_run_from_the_first_session_week_with_cumulative_counts_and_changes()
    {
        var report = PaperPhaseCheck.Evaluate(Facts());

        report.Weeks.Count.ShouldBe(13);
        report.Weeks[0].Week.ShouldBe("2026-W45");
        report.Weeks[0].Sessions.ShouldBe(5);
        report.Weeks[^1].Sessions.ShouldBe(1);
        report.Weeks[^1].CumulativeCounted.ShouldBe(61);
        report.Weeks[^1].CumulativeR.ShouldBe(6.1, 1e-9);
        report.Weeks.Sum(w => w.TradesCounted).ShouldBe(61);
        report.Weeks.Sum(w => w.RiskDownChanges).ShouldBe(2);
        report.Weeks.Single(w => w.Week == "2026-W49").RiskDownChanges.ShouldBe(1);
    }

    [Fact]
    public void The_printed_part_holds_the_split_the_zero_primary_plans_and_what_is_never_judged()
    {
        var facts = Facts(auto: 4);
        var sessions = facts.Sessions.ToList();
        sessions[3] = sessions[3] with { PrimaryTickets = 0 };

        var report = PaperPhaseCheck.Evaluate(facts with { Sessions = sessions });

        report.Printed.AutoApproved.ShouldBe(4);
        report.Printed.OwnerApproved.ShouldBe(57);
        report.Printed.PlansWithoutPrimary.ShouldBe(1);
        report.Printed.NotJudged.Select(n => n.Name).ShouldBe(["no look-ahead", "paper equals grading for the same ticket", "determinism", "no order is placed", "real-fill behaviour"]);
        report.Printed.NotJudged.ShouldAllBe(n => n.Verdict == "NOT EVALUABLE");
    }

    [Fact]
    public void The_text_is_deterministic_and_always_prints_the_limits_and_the_label()
    {
        var a = PaperPhaseText.Render(PaperPhaseCheck.Evaluate(Facts()));
        var b = PaperPhaseText.Render(PaperPhaseCheck.Evaluate(Facts()));

        b.ShouldBe(a);
        a.ShouldContain("exit criterion: MET (point estimate;");
        a.ShouldContain("limit: Simulated fills on official 1-minute bars with no real execution");
        a.ShouldContain("limit: Stored records only; nothing is written");
        a.ShouldContain("2026-W45:");
    }

    [Fact]
    public void The_service_that_is_off_is_a_blocker_with_whose_act_it_is()
    {
        var facts = Facts() with { Services = [new PhaseService("paper-capture", "the paper capture", true, "paused")] };

        var report = PaperPhaseCheck.Evaluate(facts);

        report.Printed.Blockers.ShouldContain(b => b.Contains("paper-capture", StringComparison.Ordinal) && b.Contains("owner", StringComparison.Ordinal));
    }
}
