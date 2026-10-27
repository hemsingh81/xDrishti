#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Ai.Drafting;
using XDrishti.Application.Lab;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-07 handlers over fakes: the core checks, the job (first try, repairs, invalid, decline, model down, timeout), the start rules, the owner's confirm and discard.</summary>
public sealed class DraftHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StrategyIssue Problem() => new("trigger", "strategy.bad", "bad", "error", 1, 1, 1, 2, 0, 1, null);

    // the core

    [Fact]
    public async Task Submit_accepts_a_valid_faithful_answer_with_no_finding()
    {
        var rig = new DraftRig();
        var answer = DraftAnswerReader.Read(DraftRig.Json()).Value;

        var result = await rig.Submit().HandleAsync(new SubmitDraftQuery(DraftRig.Idea, answer), Ct);

        result.Status.ShouldBe("valid");
        result.Findings.ShouldBeEmpty();
        result.NeedsRepair.ShouldBeFalse();
        result.Id.ShouldBe("DRAFT_X");
    }

    [Fact]
    public async Task Submit_marks_a_text_the_validator_refuses_as_invalid_with_its_problems()
    {
        var rig = new DraftRig { Check = () => new StrategyCheck(null, null, [Problem()]) };

        var result = await rig.Submit().HandleAsync(new SubmitDraftQuery(DraftRig.Idea, DraftAnswerReader.Read(DraftRig.Json()).Value), Ct);

        result.Status.ShouldBe("invalid");
        result.Problems.Single().Code.ShouldBe("strategy.bad");
        result.NeedsRepair.ShouldBeTrue();
    }

    [Fact]
    public async Task Submit_declines_without_text_and_flags_a_decline_with_text()
    {
        var rig = new DraftRig();

        var declined = await rig.Submit().HandleAsync(new SubmitDraftQuery("buy on news", DraftAnswerReader.Read(DraftRig.Json(null, decline: true)).Value), Ct);
        var both = await rig.Submit().HandleAsync(new SubmitDraftQuery("buy on news", DraftAnswerReader.Read(DraftRig.Json(DraftRig.Yaml, decline: true)).Value), Ct);

        declined.Status.ShouldBe("declined");
        declined.NeedsRepair.ShouldBeFalse();
        both.Findings.Single().Code.ShouldBe(DraftCodes.DeclineWithYaml);
        both.NeedsRepair.ShouldBeTrue();
    }

    [Fact]
    public async Task Submit_reports_a_missing_text_a_lying_assumption_and_a_dropped_number()
    {
        var rig = new DraftRig();
        var missing = await rig.Submit().HandleAsync(new SubmitDraftQuery(DraftRig.Idea, DraftAnswerReader.Read(DraftRig.Json(null)).Value), Ct);
        var lie = await rig.Submit().HandleAsync(new SubmitDraftQuery(DraftRig.Idea, DraftAnswerReader.Read(DraftRig.Json(assumptions: "{\"field\":\"exits[0].rr\",\"value\":\"3\",\"why\":\"x\"}")).Value), Ct);
        var dropped = await rig.Submit().HandleAsync(new SubmitDraftQuery(DraftRig.Idea + " Only in 2022.", DraftAnswerReader.Read(DraftRig.Json()).Value), Ct);

        missing.Findings.Single().Code.ShouldBe(DraftCodes.YamlMissing);
        lie.Findings.ShouldContain(f => f.Code == DraftCodes.AssumptionNotInYaml);
        dropped.Findings.Single().ShouldBe(new DraftFinding(DraftCodes.IdeaNumberDropped, "2022"));
        dropped.Status.ShouldBe("valid");
    }

    // the job

    [Fact]
    public async Task The_job_stores_a_valid_draft_after_one_model_call_and_records_the_call()
    {
        var rig = new DraftRig();
        rig.Answers.Enqueue(() => DraftRig.Reply(DraftRig.Json()));

        var result = await rig.Job().HandleAsync(new DraftStrategyCommand(1, DraftRig.Idea, null, DraftRig.Owner), Ct);

        result.Value.Status.ShouldBe("valid");
        result.Value.Attempts.ShouldBe(1);
        rig.ModelCalls.ShouldBe(1);
        rig.Store.Drafts[1].Yaml.ShouldBe(DraftRig.Yaml);
        rig.Audit.Single().Purpose.ShouldBe("draft");
        rig.Requests[0].System.ShouldContain("SHEET");
        rig.Requests[0].User.ShouldContain(DraftRig.Idea);
        rig.Requests[0].System.ShouldNotContain(DraftRig.Idea);
        rig.Strategies_AreUntouched();
    }

    [Fact]
    public async Task A_repair_turn_carries_only_the_located_problems_and_the_failing_text_then_the_draft_is_valid()
    {
        var rig = new DraftRig();
        var calls = 0;
        rig.Check = () => ++calls == 1 ? new StrategyCheck(null, null, [Problem()]) : new StrategyCheck(DraftRig.Description(), null, []);
        rig.Answers.Enqueue(() => DraftRig.Reply(DraftRig.Json()));
        rig.Answers.Enqueue(() => DraftRig.Reply(DraftRig.Json()));

        var result = await rig.Job().HandleAsync(new DraftStrategyCommand(1, DraftRig.Idea, null, DraftRig.Owner), Ct);

        result.Value.Status.ShouldBe("valid");
        result.Value.Attempts.ShouldBe(2);
        rig.Requests[1].User.ShouldContain("strategy.bad");
        rig.Requests[1].User.ShouldContain("close > vwap");
    }

    [Fact]
    public async Task A_draft_still_invalid_after_two_repairs_is_stored_invalid_with_the_last_problems()
    {
        var rig = new DraftRig { Check = () => new StrategyCheck(null, null, [Problem()]) };
        for (var i = 0; i < 3; i++)
        {
            rig.Answers.Enqueue(() => DraftRig.Reply(DraftRig.Json()));
        }

        var result = await rig.Job().HandleAsync(new DraftStrategyCommand(1, DraftRig.Idea, null, DraftRig.Owner), Ct);

        rig.ModelCalls.ShouldBe(3);
        result.Value.Status.ShouldBe("invalid");
        result.Value.Problems.Single().Code.ShouldBe("strategy.bad");
    }

    [Fact]
    public async Task A_decline_is_stored_declined_with_what_is_unsupported_and_no_text()
    {
        var rig = new DraftRig();
        rig.Answers.Enqueue(() => DraftRig.Reply(DraftRig.Json(null, decline: true)));

        var result = await rig.Job().HandleAsync(new DraftStrategyCommand(1, "buy when the news is positive and results beat", null, DraftRig.Owner), Ct);

        result.Value.Status.ShouldBe("declined");
        result.Value.Yaml.ShouldBeNull();
        result.Value.Unsupported.ShouldBe(["news"]);
        rig.ModelCalls.ShouldBe(1);
    }

    [Fact]
    public async Task An_answer_that_never_fits_the_schema_is_failed_answer_shape()
    {
        var rig = new DraftRig();
        for (var i = 0; i < 3; i++)
        {
            rig.Answers.Enqueue(() => DraftRig.Reply("I think you should buy"));
        }

        var result = await rig.Job().HandleAsync(new DraftStrategyCommand(1, DraftRig.Idea, null, DraftRig.Owner), Ct);

        result.Value.Status.ShouldBe("failed");
        result.Value.FailReason.ShouldBe("answer_shape");
    }

    [Fact]
    public async Task A_model_that_is_down_or_not_ready_is_an_honest_failed_state_and_a_ready_model_that_errors_too()
    {
        var notReady = new DraftRig { Llm = XDrishti.Application.Abstractions.Ai.LlmState.Down };
        var errors = new DraftRig();

        var first = await notReady.Job().HandleAsync(new DraftStrategyCommand(1, DraftRig.Idea, null, DraftRig.Owner), Ct);
        var second = await errors.Job().HandleAsync(new DraftStrategyCommand(1, DraftRig.Idea, null, DraftRig.Owner), Ct);

        first.Value.FailReason.ShouldBe("model_unavailable");
        notReady.ModelCalls.ShouldBe(0);
        second.Value.FailReason.ShouldBe("model_unavailable");
        errors.Audit.Single().Status.ShouldBe("error");
    }

    [Fact]
    public async Task A_job_over_its_time_is_failed_timeout_and_changes_nothing_else()
    {
        var rig = new DraftRig();
        rig.Hang = true;
        rig.Options.JobTimeoutSeconds = 1;

        var result = await rig.Job().HandleAsync(new DraftStrategyCommand(1, DraftRig.Idea, null, DraftRig.Owner), Ct);

        result.Value.FailReason.ShouldBe("timeout");
        rig.Strategies_AreUntouched();
    }

    // start

    [Fact]
    public async Task Start_answers_at_once_with_the_reserved_id_runs_the_job_on_a_scope_and_gives_the_slot_back()
    {
        var rig = new DraftRig();
        var job = new DraftRig.RecordingJob();
        var jobs = DraftRig.JobsRunning(job);

        var result = await rig.Starter(jobs).HandleAsync(new StartDraftCommand("  " + DraftRig.Idea + "  ", null, DraftRig.Owner), Ct);
        await jobs.WhenIdleAsync();

        result.Value.Status.ShouldBe("drafting");
        result.Value.Idea.ShouldBe(DraftRig.Idea);
        var seen = job.Seen.Single();
        seen.Id.ShouldBe(result.Value.Id);
        seen.Idea.ShouldBe(DraftRig.Idea);
        seen.Actor.ShouldBe(DraftRig.Owner);
        rig.Gate.Busy.ShouldBeFalse();
        jobs.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Start_refuses_a_non_owner_a_bad_idea_a_switch_that_is_off_the_day_limit_and_a_busy_model()
    {
        var rig = new DraftRig();
        (await rig.Starter().HandleAsync(new StartDraftCommand(DraftRig.Idea, null, "service:x"), Ct)).Error.Code.ShouldBe("assistant.owner_only");
        (await rig.Starter().HandleAsync(new StartDraftCommand("short", null, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.invalid");
        (await rig.Starter().HandleAsync(new StartDraftCommand(new string('x', 2001), null, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.invalid");
        (await rig.Starter().HandleAsync(new StartDraftCommand("buy when the \u0007 price rises", null, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.invalid");

        rig.Options.Enabled = false;
        (await rig.Starter().HandleAsync(new StartDraftCommand(DraftRig.Idea, null, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.disabled");
        rig.Options.Enabled = true;
        rig.Settings = rig.Settings with { Enabled = false };
        (await rig.Starter().HandleAsync(new StartDraftCommand(DraftRig.Idea, null, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.disabled");
        rig.Settings = rig.Settings with { Enabled = true };
        rig.Store.TodayCount = 3;
        (await rig.Starter().HandleAsync(new StartDraftCommand(DraftRig.Idea, null, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.limit");
        rig.Store.TodayCount = 0;
        using var held = rig.Gate.TryEnter();
        (await rig.Starter().HandleAsync(new StartDraftCommand(DraftRig.Idea, null, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.busy");
        rig.Store.NextId.ShouldBe(1);
    }

    // confirm

    [Fact]
    public async Task Confirm_stores_the_version_with_the_note_starts_a_quick_test_on_the_owners_universe_and_writes_one_decision()
    {
        var rig = new DraftRig();
        var draft = rig.Seed();
        var runId = Guid.NewGuid();
        rig.Start.HandleAsync(Arg.Any<StartLabRunCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(StartedRun(runId))));
        var basket = Guid.NewGuid();

        var result = await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(draft.Id, null, basket, null, true, DraftRig.Owner), Ct);

        result.Value.Status.ShouldBe("confirmed");
        result.Value.Confirmed.ShouldBe(new DraftConfirmed("DRAFT_X", 1, runId, false, false, null));
        await rig.Create.Received(1).HandleAsync(Arg.Is<CreateStrategyCommand>(c => c.Text == DraftRig.Yaml && c.Note == $"Assistant draft {draft.Id}; confirmed by owner" && c.ParentHash == null), Arg.Any<CancellationToken>());
        await rig.Start.Received(1).HandleAsync(Arg.Is<StartLabRunCommand>(c => c.Method == "quick" && c.BasketId == basket && c.Symbols == null && c.StrategyId == "DRAFT_X" && c.Version == 1 && c.From == null), Arg.Any<CancellationToken>());
        rig.Store.Decisions[draft.Id].Actor.ShouldBe(DraftRig.Owner);
        rig.Store.Decisions[draft.Id].Auto.ShouldBeFalse();
    }

    [Fact]
    public async Task A_second_confirm_returns_the_first_result_and_creates_and_starts_nothing()
    {
        var rig = new DraftRig();
        var draft = rig.Seed();
        rig.Start.HandleAsync(Arg.Any<StartLabRunCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(StartedRun(Guid.NewGuid()))));
        var command = new ConfirmDraftCommand(draft.Id, null, Guid.NewGuid(), null, true, DraftRig.Owner);
        var first = await rig.Confirmer().HandleAsync(command, Ct);

        var second = await rig.Confirmer().HandleAsync(command, Ct);

        second.Value.Confirmed.ShouldBe(first.Value.Confirmed);
        await rig.Create.Received(1).HandleAsync(Arg.Any<CreateStrategyCommand>(), Arg.Any<CancellationToken>());
        await rig.Start.Received(1).HandleAsync(Arg.Any<StartLabRunCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_without_a_run_stores_the_version_and_starts_no_test()
    {
        var rig = new DraftRig();
        var draft = rig.Seed();

        var result = await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(draft.Id, null, null, null, false, DraftRig.Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        await rig.Start.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Confirm_refuses_a_non_owner_and_a_switch_that_is_off_before_anything_is_read_or_written()
    {
        var rig = new DraftRig();
        var draft = rig.Seed();

        (await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(draft.Id, null, null, null, false, "service:auto"), Ct)).Error.Code.ShouldBe("assistant.owner_only");
        rig.Settings = rig.Settings with { AllowDraftConfirm = false };
        (await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(draft.Id, null, null, null, false, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.capability_off");

        await rig.Create.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        rig.Store.Decisions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("declined")]
    [InlineData("failed")]
    [InlineData("invalid")]
    public async Task Confirm_refuses_a_draft_that_is_not_valid_unless_the_owner_edited_it_into_a_valid_text(string status)
    {
        var rig = new DraftRig();
        var draft = rig.Seed(status, status == "invalid" ? "id: broken" : null);

        var result = await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(draft.Id, null, null, null, false, DraftRig.Owner), Ct);

        result.Error.Code.ShouldBe("assistant.draft_invalid");
        await rig.Create.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Confirm_revalidates_the_text_sent_so_an_edit_that_fixes_an_invalid_draft_is_stored_as_edited_and_a_bad_edit_is_refused()
    {
        var rig = new DraftRig();
        var draft = rig.Seed("invalid", "id: broken");
        rig.Check = () => new StrategyCheck(null, null, [Problem()]);

        var bad = await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(draft.Id, "id: still broken", null, null, false, DraftRig.Owner), Ct);
        rig.Check = () => new StrategyCheck(DraftRig.Description(), null, []);
        var good = await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(draft.Id, DraftRig.Yaml, null, null, false, DraftRig.Owner), Ct);

        bad.Error.Code.ShouldBe("lab.invalid_strategy");
        good.Value.Confirmed!.Edited.ShouldBeTrue();
        await rig.Create.Received(1).HandleAsync(Arg.Is<CreateStrategyCommand>(c => c.Text == DraftRig.Yaml), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_refuses_a_taken_id_and_a_missing_universe_before_anything_is_stored()
    {
        var rig = new DraftRig();
        var draft = rig.Seed();
        var run = new ConfirmDraftCommand(draft.Id, null, null, null, true, DraftRig.Owner);

        (await rig.Confirmer().HandleAsync(run, Ct)).Error.Code.ShouldBe("assistant.invalid");
        (await rig.Confirmer().HandleAsync(run with { BasketId = Guid.NewGuid(), Symbols = ["A"] }, Ct)).Error.Code.ShouldBe("assistant.invalid");
        rig.Strategies.GetAsync("DRAFT_X", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredStrategy?>(new StoredStrategy("DRAFT_X", "intraday", true, DateTimeOffset.UnixEpoch, 1, 1)));
        (await rig.Confirmer().HandleAsync(run with { BasketId = Guid.NewGuid() }, Ct)).Error.Code.ShouldBe("assistant.draft_id_taken");

        await rig.Create.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        rig.Store.Decisions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_quick_test_that_cannot_start_still_leaves_one_decision_with_the_stored_version_and_no_run()
    {
        var rig = new DraftRig();
        var draft = rig.Seed();
        rig.Start.HandleAsync(Arg.Any<StartLabRunCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure<StartedLabRun>(LabErrors.NoData(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)))));

        var result = await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(draft.Id, null, Guid.NewGuid(), null, true, DraftRig.Owner), Ct);

        result.Value.Confirmed!.LabRunId.ShouldBeNull();
        result.Value.Confirmed.StrategyId.ShouldBe("DRAFT_X");
        rig.Store.Decisions.Count.ShouldBe(1);
    }

    // discard

    [Fact]
    public async Task Discard_writes_a_decision_only_for_the_owner_and_is_repeatable_and_a_confirmed_draft_cannot_be_discarded()
    {
        var rig = new DraftRig();
        var draft = rig.Seed();

        (await rig.Discarder().HandleAsync(new DiscardDraftCommand(draft.Id, "service:x"), Ct)).Error.Code.ShouldBe("assistant.owner_only");
        (await rig.Discarder().HandleAsync(new DiscardDraftCommand(draft.Id, DraftRig.Owner), Ct)).Value.Status.ShouldBe("discarded");
        (await rig.Discarder().HandleAsync(new DiscardDraftCommand(draft.Id, DraftRig.Owner), Ct)).Value.Status.ShouldBe("discarded");
        (await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(draft.Id, null, null, null, false, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.draft_discarded");
        (await rig.Discarder().HandleAsync(new DiscardDraftCommand(99, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.draft_not_found");

        var other = rig.Seed();
        await rig.Confirmer().HandleAsync(new ConfirmDraftCommand(other.Id, null, null, null, false, DraftRig.Owner), Ct);
        (await rig.Discarder().HandleAsync(new DiscardDraftCommand(other.Id, DraftRig.Owner), Ct)).Error.Code.ShouldBe("assistant.draft_confirmed");
    }

    // reads

    [Fact]
    public async Task Get_serves_a_job_in_progress_as_drafting_and_an_unknown_id_as_not_found()
    {
        var rig = new DraftRig();
        var jobs = DraftRig.JobsRunning();
        var handler = new GetDraftHandler(rig.Store, rig.Runs, jobs);

        (await handler.HandleAsync(new GetDraftQuery(5), Ct)).Error.Code.ShouldBe("assistant.draft_not_found");
        rig.Seed();
        (await handler.HandleAsync(new GetDraftQuery(1), Ct)).Value.Status.ShouldBe("valid");
    }

    [Fact]
    public async Task List_is_bounded_and_the_run_summary_has_no_win_rate_or_verdict()
    {
        var rig = new DraftRig();
        var draft = rig.Seed();
        var run = RunOf(XDrishti.Domain.Lab.LabRunState.Succeeded);
        rig.Runs.GetAsync(run.Id, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<LabRun?>(run));
        rig.Store.Decisions[draft.Id] = new StoredDraftDecision(draft.Id, "confirmed", "DRAFT_X", 1, run.Id, false, false, DraftRig.Owner, DateTimeOffset.UnixEpoch);
        var handler = new ListDraftsHandler(rig.Store, rig.Runs);

        (await handler.HandleAsync(new ListDraftsQuery(0), Ct)).Error.Code.ShouldBe("assistant.invalid");
        var views = (await handler.HandleAsync(new ListDraftsQuery(5), Ct)).Value;

        var summary = views.Single().RunSummary!;
        summary.ShouldSatisfyAllConditions(
            s => s.State.ShouldBe("succeeded"),
            s => s.Trades.ShouldBe(12),
            s => s.OpenAtEnd.ShouldBe(1),
            s => s.Labels.ShouldContain("Quick test: exploration, not a verdict."));
        typeof(DraftRunSummary).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Win", StringComparison.Ordinal) || n.Contains("Expectancy", StringComparison.Ordinal) || n.Contains("Verdict", StringComparison.Ordinal));
    }

    private static StartedLabRun StartedRun(Guid id) => new(LabRunView.Of(RunOf(XDrishti.Domain.Lab.LabRunState.Queued, id)), true);

    private static LabRun RunOf(XDrishti.Domain.Lab.LabRunState state, Guid? id = null) => new(
        id ?? Guid.NewGuid(), new string('1', 64), "DRAFT_X", 1, new string('a', 64), new LabUniverse("basket", null, "basket", 3, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null), new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "quick",
        new string('c', 64), "sim-5", state, null, 0, 0, [], state == XDrishti.Domain.Lab.LabRunState.Succeeded ? new LabCounts(3, 2, 1, 12, 1) : null, [], [], ["Quick test: exploration, not a verdict."], null, false, DateTimeOffset.UnixEpoch, null, null);
}

internal static class DraftRigExtensions
{
    /// <summary>The model's job never reaches the strategy library or the Lab: the rig's create and start handlers are not called.</summary>
    public static void Strategies_AreUntouched(this DraftRig rig)
    {
        rig.Create.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);
        rig.Start.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);
    }
}
