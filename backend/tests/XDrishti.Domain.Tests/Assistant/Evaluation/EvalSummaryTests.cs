namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>Spec P9-09 AC 8, 9, 10, 11, 12: pass^k, stability, the verdict, the fingerprint, accept and compare.</summary>
public sealed class EvalSummaryTests
{
    private static readonly ThresholdTable Table = EvalTables.Thresholds();

    private static readonly IReadOnlyDictionary<string, bool> NoFlags = new Dictionary<string, bool>();

    private static CaseRepeatResult Data(string id, int repeat, bool passed, string answer = "a", bool hard = false) =>
        new("data", id, repeat, passed, true, null, new Dictionary<string, bool> { ["hard_fail"] = hard }, answer);

    private static List<CaseRepeatResult> DataRows(int repeats, Func<int, int, bool> passes) =>
        [.. Enumerable.Range(1, 30).SelectMany(c => Enumerable.Range(1, repeats).Select(r => Data($"DQ-{c:D2}", r, passes(c, r))))];

    private static CaseRepeatResult Plan(string id, int repeat, bool raw, bool repaired, bool escape = false, bool covered = true) =>
        new("plans", id, repeat, repaired && !escape && covered, null, null, new Dictionary<string, bool> { ["raw"] = raw, ["repaired"] = repaired, ["escape"] = escape, ["covered"] = covered }, "t");

    private static CaseRepeatResult Idea(string id, int repeat, bool firstShot, bool valid, double intent, bool? decline = null) =>
        new("ideas", id, repeat, valid && intent >= 1, null, intent, new Dictionary<string, bool> { ["first_shot"] = firstShot, ["valid"] = valid, ["decline_applicable"] = decline is not null, ["decline_correct"] = decline == true }, "t");

    private static List<CaseRepeatResult> PlanRows(int repeats, int rawOk, int repairedOk) =>
        [.. Enumerable.Range(1, 10).SelectMany(c => Enumerable.Range(1, repeats).Select(r => Plan($"PL-{c:D2}", r, c <= rawOk, c <= repairedOk)))];

    private static List<CaseRepeatResult> IdeaRows(int repeats, int firstOk, int validOk, double intent = 0.9) =>
        [.. Enumerable.Range(1, 10).SelectMany(c => Enumerable.Range(1, repeats).Select(r => Idea($"ID-{c:D2}", r, c <= firstOk, c <= validOk, intent, c >= 9 ? true : null)))];

    [Fact]
    public void A_case_passes_only_when_every_repeat_passes_and_pass_at_one_is_reported_beside_it()
    {
        var rows = DataRows(3, (c, r) => !(c == 1 && r == 3) && !(c == 2 && r == 1));

        var data = EvalSummarizer.Build(rows, Table, 3, null).Find("data")!;

        data.Rates.Single(r => r.Name == "pass").K.ShouldBe(28);
        data.Rates.Single(r => r.Name == "pass_at_1").K.ShouldBe(29);
        data.Outcome.State.ShouldBe(SetState.Pass);
    }

    [Fact]
    public void Stability_is_the_share_of_cases_whose_repeats_gave_the_same_answer()
    {
        var rows = DataRows(3, (_, _) => true).Select(r => r.CaseId == "DQ-03" && r.Repeat == 2 ? r with { AnswerKey = "different" } : r).ToList();

        var data = EvalSummarizer.Build(rows, Table, 3, null).Find("data")!;

        data.Stability!.Value.ShouldBe(29.0 / 30, 1e-12);
        EvalSummarizer.Build(DataRows(1, (_, _) => true), Table, 1, null).Find("data")!.Stability.ShouldBeNull();
    }

    [Fact]
    public void The_intervals_and_the_small_sample_sentence_are_in_the_summary()
    {
        var summary = EvalSummarizer.Build(DataRows(3, (c, _) => c <= 27), Table, 3, null);

        var pass = summary.Find("data")!.Rates.Single(r => r.Name == "pass");

        (pass.K, pass.N).ShouldBe((27, 30));
        pass.Low.ShouldBe(0.744, 0.001);
        pass.High.ShouldBe(0.965, 0.001);
        summary.SmallSampleLine.ShouldBe("30 cases cannot separate 27 from 24 correct");
    }

    [Fact]
    public void A_gating_pass_needs_data_plans_and_ideas_and_events_not_evaluable_does_not_block()
    {
        var rows = DataRows(3, (c, _) => c <= 28).Concat(PlanRows(3, 8, 9)).Concat(IdeaRows(3, 8, 9)).ToList();

        var summary = EvalSummarizer.Build(rows, Table, 3, AnnouncementGrader.Grade([], Table));

        summary.Verdict.ShouldBe(EvalVerdict.Pass);
        summary.NonGating.ShouldBeFalse();
        summary.Sets.Select(s => (s.Set, s.Outcome.State)).ShouldBe([("data", SetState.Pass), ("plans", SetState.Pass), ("ideas", SetState.Pass), ("events", SetState.NotEvaluable)]);
    }

    [Fact]
    public void One_failing_set_fails_the_run_and_a_missing_set_leaves_it_incomplete()
    {
        var failing = DataRows(3, (c, _) => c <= 28).Concat(PlanRows(3, 7, 9)).Concat(IdeaRows(3, 8, 9)).ToList();
        var missing = DataRows(3, (_, _) => true).Concat(PlanRows(3, 10, 10)).ToList();

        EvalSummarizer.Build(failing, Table, 3, null).Verdict.ShouldBe(EvalVerdict.Fail);
        EvalSummarizer.Build(missing, Table, 3, null).Verdict.ShouldBe(EvalVerdict.Incomplete);
    }

    [Fact]
    public void One_repeat_is_non_gating()
    {
        var rows = DataRows(1, (_, _) => true).Concat(PlanRows(1, 10, 10)).Concat(IdeaRows(1, 10, 10, 1.0)).ToList();

        var summary = EvalSummarizer.Build(rows, Table, 1, null);

        summary.Verdict.ShouldBe(EvalVerdict.Pass);
        summary.NonGating.ShouldBeTrue();
    }

    [Fact]
    public void An_ungrounded_figure_in_an_honest_case_fails_the_whole_data_set_even_with_thirty_passes()
    {
        var rows = DataRows(3, (_, _) => true).Select(r => r.CaseId == "DQ-28" && r.Repeat == 1 ? r with { Flags = new Dictionary<string, bool> { ["hard_fail"] = true } } : r).ToList();

        var data = EvalSummarizer.Build(rows, Table, 3, null).Find("data")!;

        data.Outcome.State.ShouldBe(SetState.Fail);
        data.Outcome.Reasons.ShouldContain("an honest-state question has an ungrounded figure");
    }

    [Fact]
    public void Plans_count_an_escape_from_any_repeat_and_ideas_average_the_intent_per_idea()
    {
        var plans = PlanRows(3, 10, 10).Select(r => r.CaseId == "PL-04" && r.Repeat == 2 ? r with { Flags = new Dictionary<string, bool> { ["raw"] = true, ["repaired"] = true, ["escape"] = true, ["covered"] = true } } : r).ToList();
        var ideas = IdeaRows(3, 10, 10, 0.9).Select(r => r.CaseId == "ID-05" ? r with { Score = r.Repeat == 1 ? 0.3 : 0.6 } : r).ToList();

        var summary = EvalSummarizer.Build(plans.Concat(ideas).ToList(), Table, 3, null);

        summary.Find("plans")!.Outcome.Reasons.ShouldContain("escape 1 is above 0");
        summary.Find("ideas")!.IntentLowest!.Value.ShouldBe(0.5, 1e-12);
        summary.Find("ideas")!.Outcome.State.ShouldBe(SetState.Fail);
    }

    private static FingerprintInputs Inputs() => new("model-a", "runtime-a", [new PromptStamp("chat", "v1", "h1"), new PromptStamp("briefing", "v1", "h2")], "sets", "thresholds", "fixture", "main");

    [Fact]
    public void Every_input_of_the_fingerprint_changes_it()
    {
        var baseline = EvalFingerprint.Of(Inputs());
        var variants = new[]
        {
            Inputs() with { ModelSha256 = "model-b" },
            Inputs() with { RuntimeDigest = "runtime-b" },
            Inputs() with { Prompts = [new PromptStamp("chat", "v2", "h1"), new PromptStamp("briefing", "v1", "h2")] },
            Inputs() with { Prompts = [new PromptStamp("chat", "v1", "hX"), new PromptStamp("briefing", "v1", "h2")] },
            Inputs() with { Prompts = [new PromptStamp("chat", "v1", "h1")] },
            Inputs() with { SetsHash = "sets2" },
            Inputs() with { ThresholdsHash = "thresholds2" },
            Inputs() with { FixtureHash = "fixture2" },
            Inputs() with { Profile = "gptoss" },
        };

        baseline.Length.ShouldBe(64);
        EvalFingerprint.Of(Inputs()).ShouldBe(baseline);
        variants.Select(EvalFingerprint.Of).Distinct().Count().ShouldBe(variants.Length);
        variants.ShouldAllBe(v => EvalFingerprint.Of(v) != baseline);
    }

    [Fact]
    public void The_differences_name_what_changed()
    {
        var now = Inputs() with { ModelSha256 = "model-b", Prompts = [new PromptStamp("chat", "v2", "h9"), new PromptStamp("briefing", "v1", "h2"), new PromptStamp("proposal", "v1", "h3")], ThresholdsHash = "t2" };

        EvalFingerprint.Differences(now, Inputs()).ShouldBe(["model", "prompt chat", "prompt proposal", "thresholds"]);
        EvalFingerprint.Differences(Inputs(), Inputs()).ShouldBeEmpty();
    }

    private static RunSummary Summary(string verdict, bool nonGating, int data, int plans, int ideas) =>
        new(verdict, nonGating, [Set("data", "pass", data), Set("plans", "repaired_pass", plans), Set("ideas", "repaired_valid", ideas)], string.Empty);

    private static SetSummary Set(string set, string rate, int k) => new(set, SetOutcome.Of(set, SetState.Pass), [RateLine.Of(rate, k, 30)], 30, 3, null, null, null);

    [Fact]
    public void Accept_is_refused_for_a_non_gating_run_and_for_a_run_that_is_not_pass()
    {
        EvalAcceptance.Check(Summary(EvalVerdict.Pass, true, 28, 9, 9), null, null).Error.Code.ShouldBe(EvalAcceptance.NonGating);
        EvalAcceptance.Check(Summary(EvalVerdict.Fail, false, 20, 9, 9), null, null).Error.Code.ShouldBe(EvalAcceptance.BelowThreshold);
        EvalAcceptance.Check(Summary(EvalVerdict.Incomplete, false, 28, 9, 9), null, null).Error.Code.ShouldBe(EvalAcceptance.BelowThreshold);
        EvalAcceptance.Check(Summary(EvalVerdict.Pass, false, 28, 9, 9), null, null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_set_that_got_worse_than_the_baseline_needs_a_reason()
    {
        var baseline = Summary(EvalVerdict.Pass, false, 29, 10, 10);
        var worse = Summary(EvalVerdict.Pass, false, 28, 10, 10);

        EvalAcceptance.Check(worse, baseline, null).Error.Code.ShouldBe(EvalAcceptance.ReasonRequired);
        EvalAcceptance.Check(worse, baseline, "  ").Error.Code.ShouldBe(EvalAcceptance.ReasonRequired);
        EvalAcceptance.Check(worse, baseline, "a smaller model, same answers").IsSuccess.ShouldBeTrue();
        EvalAcceptance.Check(Summary(EvalVerdict.Pass, false, 29, 10, 10), baseline, null).IsSuccess.ShouldBeTrue();
        EvalAcceptance.Check(worse, baseline, new string('x', 501)).Error.Code.ShouldBe(EvalAcceptance.ReasonRequired);
        EvalAcceptance.Worse(worse, baseline).ShouldBe(["data 28 < 29"]);
    }

    [Fact]
    public void A_comparison_counts_discordant_pairs_and_flags_a_regression_by_the_rule_of_each_set()
    {
        IReadOnlyDictionary<string, bool> Cases(string prefix, int count, Func<int, bool> pass) => Enumerable.Range(1, count).ToDictionary(i => $"{prefix}-{i:D2}", pass);
        var baseline = new Dictionary<string, IReadOnlyDictionary<string, bool>> { ["data"] = Cases("DQ", 30, _ => true), ["plans"] = Cases("PL", 10, _ => true), ["ideas"] = Cases("ID", 10, _ => true) };
        var now = new Dictionary<string, IReadOnlyDictionary<string, bool>> { ["data"] = Cases("DQ", 30, i => i > 1), ["plans"] = Cases("PL", 10, i => i > 1), ["ideas"] = Cases("ID", 10, _ => true) };
        var summary = EvalSummarizer.Build(DataRows(3, (c, _) => c > 1).Concat(PlanRows(3, 9, 9)).Concat(IdeaRows(3, 10, 10, 1.0)).ToList(), Table, 3, null);

        var sets = RunComparison.Compare(baseline, now, summary);

        sets.Single(s => s.Set == "data").Regression.ShouldBeFalse("one discordant pair on data is below the rule of two");
        sets.Single(s => s.Set == "plans").Regression.ShouldBeTrue("one on plans is the rule");
        sets.Single(s => s.Set == "ideas").Regression.ShouldBeFalse();
        var data = sets.Single(s => s.Set == "data");
        (data.Paired.B, data.Paired.C, data.PassesNow, data.PassesBefore).ShouldBe((1, 0, 29, 30));
        data.Sentence.ShouldBe("with this many cases at least 6 discordant pairs, all one way, are needed for p < 0.05");
    }
}
