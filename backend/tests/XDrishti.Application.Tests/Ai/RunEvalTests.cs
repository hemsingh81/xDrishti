#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Application.Tests.Ai;

/// <summary>
/// Spec P9-09 AC 8, 10, 11, 13 and the scripted end to end of the test plan: a fake model that answers everything perfectly gives a PASS that is stored, accepted and then made stale by a changed prompt version; a refusal never reaches the model; a model that goes down is
/// never a pass. No test calls a real model.
/// </summary>
public sealed class RunEvalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RunEvalCommand All(int repeats = 3) => new(["all"], repeats, RunEvalCommand.Builtin, EvalErrors.CliActor);

    [Fact]
    public async Task A_perfect_scripted_run_is_a_stored_gating_pass_that_the_owner_accepts_and_a_changed_prompt_then_makes_stale()
    {
        var rig = new EvalRig();
        var status = rig.Status();

        var result = await rig.Run().HandleAsync(All(), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var run = result.Value;
        (run.Status, run.Verdict, run.Gating, run.Actor).ShouldBe(("completed", "PASS", true, "cli:xd"));
        rig.Answers.Calls.ShouldBe(90);
        rig.Answers.LastActor.ShouldBe(EvalErrors.RunActor(run.Id));
        rig.WriterCalls.ShouldBe(30);
        rig.Store.Cases[run.Id].Count.ShouldBe(150);
        run.Summary.Sets.Select(s => (s.Set, s.Outcome.State)).ShouldBe([("data", SetState.Pass), ("plans", SetState.Pass), ("ideas", SetState.Pass), ("events", SetState.NotEvaluable)]);
        run.Fingerprint.Length.ShouldBe(64);
        (await status.HandleAsync(new GetEvalStatusQuery(), Ct)).ShouldSatisfyAllConditions(
            s => s.State.ShouldBe("not_yet_evaluated"),
            s => s.Detail.ShouldBe("no accepted eval run"),
            s => s.Stale.ShouldBeFalse());

        var accepted = await rig.Accept().HandleAsync(new AcceptEvalCommand(run.Id, null, EvalErrors.CliActor), Ct);

        accepted.IsSuccess.ShouldBeTrue(accepted.IsFailure ? accepted.Error.Message : string.Empty);
        accepted.Value.IsBaseline.ShouldBeTrue();
        var fresh = await status.HandleAsync(new GetEvalStatusQuery(), Ct);
        (fresh.State, fresh.Stale, fresh.BaselineRunId, fresh.Reasons.Count).ShouldBe(("passed", false, run.Id, 0));
        fresh.CurrentFingerprint.ShouldBe(run.Fingerprint);

        rig.Prompts[0] = rig.Prompts[0] with { Version = "v2" };
        var stale = await status.HandleAsync(new GetEvalStatusQuery(), Ct);
        (stale.State, stale.Stale).ShouldBe(("not_yet_evaluated", true));
        stale.Reasons.ShouldBe(["prompt chat"]);
        stale.CurrentFingerprint.ShouldNotBe(run.Fingerprint);

        rig.Prompts[0] = rig.Prompts[0] with { Version = "v1" };
        rig.Model = rig.Model with { Sha256 = new string('b', 64) };
        (await status.HandleAsync(new GetEvalStatusQuery(), Ct)).Reasons.ShouldBe(["model"]);

        rig.Model = rig.Model with { Known = false, State = XDrishti.Application.Abstractions.Ai.LlmState.Down };
        var off = await status.HandleAsync(new GetEvalStatusQuery(), Ct);
        (off.Stale, off.State).ShouldBe((false, "passed"));
        off.Detail.ShouldContain("not running");
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData(null)]
    public async Task A_missing_or_different_marker_aborts_before_any_model_call(string? marker)
    {
        var rig = new EvalRig { Marker = marker };

        var result = await rig.Run().HandleAsync(All(), Ct);

        result.Error.Code.ShouldBe("eval.wrong_database");
        (rig.Answers.Calls, rig.WriterCalls, rig.DrafterCalls, rig.Store.Runs.Count).ShouldBe((0, 0, 0, 0));
    }

    [Fact]
    public async Task The_runner_refuses_while_the_assistant_is_off_the_model_is_down_or_the_nightly_run_goes_and_asks_the_model_nothing()
    {
        var off = new EvalRig { Settings = new(7, false, false, 6) };
        var down = new EvalRig { Model = new(false, XDrishti.Application.Abstractions.Ai.LlmState.Down, "x", string.Empty, string.Empty) };
        var nightly = new EvalRig { NightlyRunning = true };

        (await off.Run().HandleAsync(All(), Ct)).Error.Code.ShouldBe("eval.assistant_disabled");
        (await down.Run().HandleAsync(All(), Ct)).Error.Code.ShouldBe("eval.model_unavailable");
        (await nightly.Run().HandleAsync(All(), Ct)).Error.Code.ShouldBe("eval.nightly_running");
        new[] { off, down, nightly }.ShouldAllBe(r => r.Answers.Calls == 0 && r.WriterCalls == 0 && r.Store.Runs.Count == 0);
    }

    [Theory]
    [InlineData("nothing", 3, "builtin")]
    [InlineData("data", 0, "builtin")]
    [InlineData("data", 6, "builtin")]
    [InlineData("data", 3, "hermes")]
    public async Task A_wrong_command_is_refused_as_invalid(string set, int repeats, string target)
    {
        var rig = new EvalRig();

        var result = await rig.Run().HandleAsync(new RunEvalCommand([set], repeats, target, EvalErrors.CliActor), Ct);

        result.Error.Code.ShouldBe("assistant.invalid");
        rig.Answers.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_model_that_goes_down_mid_run_is_stored_as_model_unavailable_and_is_never_a_pass()
    {
        var rig = new EvalRig();
        rig.Answers.FailAfter = 45;

        var result = await rig.Run().HandleAsync(new RunEvalCommand(["data"], 3, RunEvalCommand.Builtin, EvalErrors.CliActor), Ct);

        var run = result.Value;
        (run.Status, run.Verdict, run.Gating).ShouldBe(("model_unavailable", "INCOMPLETE", false));
        rig.Store.Cases[run.Id].Count.ShouldBe(45);
        (await rig.Accept().HandleAsync(new AcceptEvalCommand(run.Id, null, EvalErrors.CliActor), Ct)).Error.Code.ShouldBe("eval.non_gating");
    }

    [Fact]
    public async Task A_model_that_is_down_at_the_first_question_stores_nothing()
    {
        var rig = new EvalRig();
        rig.Answers.FailAfter = 0;

        var result = await rig.Run().HandleAsync(new RunEvalCommand(["data"], 3, RunEvalCommand.Builtin, EvalErrors.CliActor), Ct);

        result.Error.Code.ShouldBe("eval.model_unavailable");
        rig.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_repeat_is_a_non_gating_look_that_can_never_be_accepted()
    {
        var rig = new EvalRig();

        var run = (await rig.Run().HandleAsync(All(1), Ct)).Value;

        (run.Verdict, run.Gating, run.Summary.NonGating).ShouldBe(("PASS", false, true));
        (await rig.Accept().HandleAsync(new AcceptEvalCommand(run.Id, null, EvalErrors.CliActor), Ct)).Error.Code.ShouldBe("eval.non_gating");
        rig.Store.Baselines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Wrong_answers_below_the_threshold_fail_the_run_and_it_cannot_be_accepted()
    {
        var rig = new EvalRig();
        rig.Answers.Override = (c, _) => int.Parse(c.Id[3..], System.Globalization.CultureInfo.InvariantCulture) <= 4 ? "I do not know." : null;

        var run = (await rig.Run().HandleAsync(All(), Ct)).Value;

        run.Verdict.ShouldBe("FAIL");
        run.Summary.Find("data")!.Outcome.Reasons.ShouldContain("data pass 26 of 30 is below 27");
        (await rig.Accept().HandleAsync(new AcceptEvalCommand(run.Id, null, EvalErrors.CliActor), Ct)).Error.Code.ShouldBe("eval.below_threshold");
    }

    [Fact]
    public async Task A_plan_text_with_a_number_not_in_the_facts_is_refused_twice_and_the_plan_set_fails_because_one_plan_is_not_covered()
    {
        var rig = new EvalRig();
        rig.Wrong = (set, id) => set == "plans" && id == "PL-01" ? "Plan for 2026-03-03 expects 0.99R on EVA." : null;

        var run = (await rig.Run().HandleAsync(new RunEvalCommand(["plans"], 3, RunEvalCommand.Builtin, EvalErrors.CliActor), Ct)).Value;

        var plans = run.Summary.Find("plans")!;
        plans.Rates.Single(r => r.Name == "repaired_pass").K.ShouldBe(9);
        plans.Rates.Single(r => r.Name == "raw_pass").K.ShouldBe(9);
        plans.Rates.Single(r => r.Name == "covered").K.ShouldBe(9);
        plans.Outcome.State.ShouldBe(SetState.Fail);
        plans.Outcome.Reasons.ShouldBe(["covered 9 of 10"]);
        run.Verdict.ShouldBe("FAIL");
        rig.WriterCalls.ShouldBe(33, "a second attempt for each repeat of the refused plan, and that one is refused as well");
    }

    [Fact]
    public async Task The_accept_needs_a_reason_when_a_set_got_worse_and_the_compare_names_the_discordant_cases()
    {
        var rig = new EvalRig();
        var first = (await rig.Run().HandleAsync(All(), Ct)).Value;
        (await rig.Accept().HandleAsync(new AcceptEvalCommand(first.Id, null, EvalErrors.CliActor), Ct)).IsSuccess.ShouldBeTrue();
        rig.Answers.Override = (c, _) => c.Id == "DQ-05" ? "I do not know." : null;
        var second = (await rig.Run().HandleAsync(All(), Ct)).Value;

        var refused = await rig.Accept().HandleAsync(new AcceptEvalCommand(second.Id, null, EvalErrors.CliActor), Ct);
        var compare = await new CompareEvalsHandler(rig.Store).HandleAsync(new CompareEvalsQuery(second.Id, null), Ct);

        second.Verdict.ShouldBe("PASS");
        refused.Error.Code.ShouldBe("eval.reason_required");
        (await rig.Accept().HandleAsync(new AcceptEvalCommand(second.Id, "a smaller model", EvalErrors.CliActor), Ct)).IsSuccess.ShouldBeTrue();
        compare.Value.BaselineId.ShouldBe(first.Id);
        var data = compare.Value.Sets.Single(s => s.Set == "data");
        (data.Paired.B, data.Paired.C, data.PassesNow, data.PassesBefore, data.Regression).ShouldBe((1, 0, 29, 30, false));
        (await rig.Accept().HandleAsync(new AcceptEvalCommand(first.Id, null, "service:eval"), Ct)).Error.Code.ShouldBe("assistant.owner_only");
    }

    [Fact]
    public async Task Compare_without_a_baseline_says_so_and_a_run_that_is_not_stored_is_not_found()
    {
        var rig = new EvalRig();
        var run = (await rig.Run().HandleAsync(All(1), Ct)).Value;
        var compare = new CompareEvalsHandler(rig.Store);

        (await compare.HandleAsync(new CompareEvalsQuery(run.Id, null), Ct)).Error.Code.ShouldBe("eval.no_baseline");
        (await compare.HandleAsync(new CompareEvalsQuery(Guid.NewGuid(), null), Ct)).Error.Code.ShouldBe("assistant.eval_not_found");
        (await new GetEvalHandler(rig.Store).HandleAsync(new GetEvalQuery(Guid.NewGuid()), Ct)).Error.Code.ShouldBe("assistant.eval_not_found");
        (await new GetEvalHandler(rig.Store).HandleAsync(new GetEvalQuery(run.Id), Ct)).Value.Cases.Count.ShouldBe(50);
        (await new ListEvalsHandler(rig.Store).HandleAsync(new ListEvalsQuery(0), Ct)).Error.Code.ShouldBe("assistant.invalid");
        (await new ListEvalsHandler(rig.Store).HandleAsync(new ListEvalsQuery(), Ct)).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Answers_graded_from_a_file_are_grounded_by_the_audit_of_their_session_and_never_gating()
    {
        var rig = new EvalRig();
        var session = Guid.NewGuid();
        var audit = Substitute.For<IConversationReader>();
        audit.GetAsync(session, Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<ConversationDetail?>(new ConversationDetail(session, "t", "eval", DateTimeOffset.UnixEpoch, "m", null,
            [new MessageView(1, Guid.NewGuid(), 2, "assistant", "x", DateTimeOffset.UnixEpoch, "stop", "grounded", [], null, null, null, null, null, false, [new ToolCallSummary(5, 1, "run_sql", JsonDocument.Parse("{}").RootElement, "ok", null, 1, false, 1, null)])])));
        audit.GetToolCallAsync(5, Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<StoredToolCall?>(new StoredToolCall(5, session, Guid.NewGuid(), 1, "run_sql", JsonDocument.Parse("{}").RootElement, "ok", null, JsonDocument.Parse("""{"n":24}""").RootElement, 8, 1, false, null, 0, 0, DateTimeOffset.UnixEpoch, 1)));
        var handler = new GradeEvalAnswersHandler(new DiskEvalFiles(), audit, rig.Store, new EvalRig.FakePrompts(rig), rig.Time, NullLogger<GradeEvalAnswersHandler>.Instance);
        var lines = $$"""
            {"set":"data","case_id":"DQ-01","repeat":1,"answer":"ORB 15m has 24 trades.","session_id":"{{session}}","started_at":"2026-10-28T10:00:00Z","finished_at":"2026-10-28T10:00:03Z"}
            {"set":"data","case_id":"DQ-19","repeat":1,"answer":"MyLongTerm has 2 holdings.","session_id":null,"started_at":"2026-10-28T10:00:00Z","finished_at":"2026-10-28T10:00:03Z"}
            """;

        var result = await handler.HandleAsync(new GradeEvalAnswersCommand(lines, "hermes", EvalErrors.CliActor), Ct);

        var run = result.Value;
        (run.Target, run.Gating, run.Status, run.Verdict).ShouldBe(("hermes", false, "completed", "INCOMPLETE"));
        var cases = rig.Store.Cases[run.Id];
        cases.Single(c => c.CaseId == "DQ-01").ShouldSatisfyAllConditions(c => c.Passed.ShouldBeTrue(), c => c.Grounded.ShouldBe(true), c => c.LatencyMs.ShouldBe(3000));
        cases.Single(c => c.CaseId == "DQ-19").ShouldSatisfyAllConditions(c => c.Passed.ShouldBeFalse(), c => c.Grounded.ShouldBeNull());
        (await handler.HandleAsync(new GradeEvalAnswersCommand(lines, "builtin", EvalErrors.CliActor), Ct)).Error.Code.ShouldBe("assistant.invalid");
        (await handler.HandleAsync(new GradeEvalAnswersCommand("nonsense", "hermes", EvalErrors.CliActor), Ct)).Error.Code.ShouldBe("assistant.invalid");
        (await handler.HandleAsync(new GradeEvalAnswersCommand("""{"set":"plans","case_id":"PL-01","repeat":1,"answer":"x"}""", "hermes", EvalErrors.CliActor), Ct)).Error.Code.ShouldBe("assistant.invalid");
    }

    [Fact]
    public async Task The_reports_hold_the_rates_with_their_intervals_the_small_sample_sentence_and_the_failed_cases()
    {
        var rig = new EvalRig();
        rig.Answers.Override = (c, _) => c.Id == "DQ-02" ? "I do not know." : null;
        var run = (await rig.Run().HandleAsync(All(), Ct)).Value;
        var detail = (await new GetEvalHandler(rig.Store).HandleAsync(new GetEvalQuery(run.Id), Ct)).Value;

        var markdown = EvalReports.Markdown(detail);
        var json = EvalReports.Json(detail);
        var answers = EvalReports.Answers(detail);

        markdown.ShouldContain("30 cases cannot separate 27 from 24 correct");
        markdown.ShouldContain("| pass | 29 of 30 |");
        markdown.ShouldContain("DQ-02");
        JsonDocument.Parse(json).RootElement.GetProperty("run").GetProperty("verdict").GetString().ShouldBe("PASS");
        answers.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(150);
    }
}
