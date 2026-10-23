using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The phase check (spec P6-18): each exit criterion has its three outcomes by a hand table, the limits are always printed, and MET is never printed for what the data cannot carry.</summary>
public sealed class PhaseCheckTests
{
    private static readonly DateOnly Held = new(2026, 4, 1);

    private static PhaseModelFacts Model(ValidationOutcome verdict = ValidationOutcome.Eligible, int folds = 6, double ece = 3.2, string[]? championReasons = null, string[]? reasons = null) =>
        new(7, "champion", verdict, folds, folds - 1, 4, verdict == ValidationOutcome.Eligible, ece, Held, new DateOnly(2024, 8, 1), new DateOnly(2026, 9, 1), reasons ?? [], championReasons ?? []);

    private static PhaseReplayFacts Replay(long compareMs = 1500, double? supplied = null, string stored = "Better", string measured = "Better") =>
        new(3, 4200, compareMs, 1400, stored, measured, supplied);

    private static PhaseVerdict V(PhaseCheckReport report, int number) => report.Criteria.Single(c => c.Number == number).Verdict;

    [Fact]
    public void Without_a_model_the_model_criteria_are_not_evaluable()
    {
        var report = PhaseCheck.Evaluate(null, null);

        V(report, 1).ShouldBe(PhaseVerdict.NotEvaluable);
        V(report, 2).ShouldBe(PhaseVerdict.NotEvaluable);
        V(report, 3).ShouldBe(PhaseVerdict.NotEvaluable);
        report.Criteria[0].Detail.ShouldContain("no model is registered");
    }

    [Theory]
    [InlineData(ValidationOutcome.CannotBeatBaselines, PhaseVerdict.NotMet)]
    [InlineData(ValidationOutcome.Inconclusive, PhaseVerdict.NotEvaluable)]
    [InlineData(ValidationOutcome.Eligible, PhaseVerdict.Met)]
    public void The_stored_verdict_decides_the_baseline_criterion(ValidationOutcome verdict, PhaseVerdict expected)
    {
        var report = PhaseCheck.Evaluate(Model(verdict, reasons: verdict == ValidationOutcome.Eligible ? [] : ["only 3 folds"]), null);

        V(report, 1).ShouldBe(expected);
    }

    [Fact]
    public void An_eligible_verdict_the_champion_rule_refuses_is_not_met()
    {
        var report = PhaseCheck.Evaluate(Model(championReasons: ["No hold-out was kept."]), null);

        V(report, 1).ShouldBe(PhaseVerdict.NotEvaluable);
        report.Criteria[0].Detail.ShouldContain("No hold-out was kept.");
    }

    [Theory]
    [InlineData(7.0, 6, PhaseVerdict.NotMet)]
    [InlineData(7.0, 3, PhaseVerdict.NotMet)]
    [InlineData(5.0, 6, PhaseVerdict.Met)]
    [InlineData(3.0, 6, PhaseVerdict.Met)]
    [InlineData(3.0, 3, PhaseVerdict.NotEvaluable)]
    [InlineData(double.NaN, 6, PhaseVerdict.NotEvaluable)]
    public void Calibration_is_judged_against_the_limit_and_needs_enough_folds_to_be_met(double ece, int folds, PhaseVerdict expected)
    {
        var report = PhaseCheck.Evaluate(Model(folds: folds, ece: ece), null);

        V(report, 2).ShouldBe(expected);
    }

    [Fact]
    public void A_stored_experiment_whose_runs_cannot_be_read_is_not_evaluable_and_says_why()
    {
        var report = PhaseCheck.Evaluate(null, null, replayProblem: "a stored run of the experiment is not found");

        V(report, 3).ShouldBe(PhaseVerdict.NotEvaluable);
        report.Criteria[2].Detail.ShouldContain("not found");
    }

    [Fact]
    public void A_measured_comparison_alone_is_never_met_because_the_replay_was_not_measured()
    {
        var report = PhaseCheck.Evaluate(Model(), Replay());

        V(report, 3).ShouldBe(PhaseVerdict.NotEvaluable);
        report.Criteria[2].Detail.ShouldContain("1.50 s");
        report.Criteria[2].Detail.ShouldContain("214 instruments");
    }

    [Fact]
    public void A_comparison_that_alone_exceeds_ten_minutes_is_not_met()
    {
        V(PhaseCheck.Evaluate(Model(), Replay(compareMs: 600_000)), 3).ShouldBe(PhaseVerdict.NotMet);
    }

    [Theory]
    [InlineData(300.0, "Better", "Better", PhaseVerdict.Met)]
    [InlineData(598.0, "Better", "Better", PhaseVerdict.Met)]
    [InlineData(599.0, "Better", "Better", PhaseVerdict.NotMet)]
    [InlineData(700.0, "Better", "Better", PhaseVerdict.NotMet)]
    [InlineData(300.0, "Better", "NotBetter", PhaseVerdict.NotEvaluable)]
    public void With_a_supplied_replay_time_the_total_is_judged_against_ten_minutes_and_the_outcome_must_reproduce(double supplied, string stored, string measured, PhaseVerdict expected)
    {
        var report = PhaseCheck.Evaluate(Model(), Replay(supplied: supplied, stored: stored, measured: measured));

        V(report, 3).ShouldBe(expected);
        report.Criteria[2].Detail.ShouldContain("as supplied by the owner");
    }

    public static TheoryData<PhaseModelFacts?, PhaseReplayFacts?> Scenarios => new()
    {
        { null, null },
        { Model(ValidationOutcome.CannotBeatBaselines, ece: 9), Replay() },
        { Model() with { HoldOutStart = null }, Replay(supplied: 100) },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_limits_are_always_printed_and_the_text_is_deterministic(PhaseModelFacts? model, PhaseReplayFacts? replay)
    {
        var first = PhaseCheck.Evaluate(model, replay).ToText();
        var second = PhaseCheck.Evaluate(model, replay).ToText();

        second.ShouldBe(first);
        first.ShouldContain("limit: about 2.1 years of history (ISS-19)");
        first.ShouldContain("about 3 usable folds");
        first.ShouldContain("214 instruments");
        first.ShouldContain("modelled");
        first.ShouldContain("summary:");
    }

    [Fact]
    public void A_model_without_a_hold_out_says_so_and_a_kept_hold_out_is_not_read()
    {
        var none = PhaseCheck.Evaluate(Model() with { HoldOutStart = null }, null).ToText();
        var kept = PhaseCheck.Evaluate(Model(), null).ToText();

        none.ShouldContain("kept no hold-out");
        kept.ShouldContain("does not read it");
    }

    [Fact]
    public void A_report_with_nothing_stored_prints_no_met()
    {
        var text = PhaseCheck.Evaluate(null, null).ToText();

        text.ShouldNotContain(". MET");
        text.ShouldContain("0 met, 0 not met, 3 not evaluable");
    }
}
