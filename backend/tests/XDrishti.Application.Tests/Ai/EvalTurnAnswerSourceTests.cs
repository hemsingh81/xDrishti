#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-09 AC 8 and 13: the eval asks as Origin eval, reads the tool results back from the audit for grounding, maps a model that failed to model_unavailable, and the turn is built over the EVAL catalog with the options fixed for the run.</summary>
public sealed class EvalTurnAnswerSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Runner(Func<ITurnSession> session, Error? refusal = null) : IAssistantTurnRunner
    {
        public TurnRequest? Request { get; private set; }

        public ValueTask<Result<ITurnSession>> BeginAsync(TurnRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return ValueTask.FromResult(refusal is null ? Result.Success(session()) : Result.Failure<ITurnSession>(refusal));
        }
    }

    private sealed class Session(Guid conversation, long[] calls, string finish = "stop") : ITurnSession
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async ValueTask<TurnResult> RunAsync(Func<TurnEvent, ValueTask> sink, CancellationToken cancellationToken)
        {
            await sink(new TurnStarted(conversation, Guid.NewGuid(), 1, "h", new TurnModel("m", null), 7));
            foreach (var id in calls)
            {
                await sink(new TurnToolResult(1, id, "ok", 1, false, 5, null, null));
            }

            return new TurnResult(9, "ORB 15m has 24 trades.", new TurnGrounding("grounded", 1, []), calls.Length, new TurnUsage(120, 30), finish, 40, 900);
        }
    }

    [Fact]
    public async Task The_question_is_asked_as_an_eval_turn_and_the_tool_results_come_back_from_the_audit()
    {
        var conversation = Guid.NewGuid();
        var runner = new Runner(() => new Session(conversation, [11, 12]));
        var audit = Substitute.For<IConversationReader>();
        audit.GetToolCallAsync(11, Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<StoredToolCall?>(Stored(11, conversation, """{"n":24}""")));
        audit.GetToolCallAsync(12, Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<StoredToolCall?>(Stored(12, conversation, null)));

        var result = await new EvalTurnAnswerSource(runner, audit).AskAsync("How many trades?", "eval:abc", Ct);

        runner.Request.ShouldBe(new TurnRequest("How many trades?", null, "eval", "eval:abc"));
        var answer = result.Value;
        (answer.Text, answer.SessionId, answer.ToolCalls, answer.PromptTokens, answer.CompletionTokens, answer.FinishReason).ShouldBe(("ORB 15m has 24 trades.", conversation, 2, 120, 30, "stop"));
        answer.ToolResults.ShouldBe(["""{"n":24}"""]);
    }

    [Fact]
    public async Task A_refusal_of_the_turn_and_a_model_that_failed_are_failures_never_answers()
    {
        var audit = Substitute.For<IConversationReader>();
        var refused = new Runner(() => throw new InvalidOperationException(), new Error("assistant.model_unavailable", "down", ErrorKind.Upstream));
        var failed = new Runner(() => new Session(Guid.NewGuid(), [], "error"));

        (await new EvalTurnAnswerSource(refused, audit).AskAsync("q", "eval:x", Ct)).Error.Code.ShouldBe("assistant.model_unavailable");
        (await new EvalTurnAnswerSource(failed, audit).AskAsync("q", "eval:x", Ct)).Error.Code.ShouldBe("assistant.model_unavailable");
    }

    [Fact]
    public void The_eval_turn_runner_is_built_over_the_eval_catalog_with_the_options_fixed_for_the_run()
    {
        var evalCatalog = Substitute.For<IEvalToolCatalog>();
        var production = Substitute.For<IToolCatalog>();
        var provider = new Services(new Dictionary<Type, object>
        {
            [typeof(TurnOptions)] = new TurnOptions { Temperature = 0.7, Seed = null, HistoryMessages = 6 },
            [typeof(IAssistantSettingsStore)] = Substitute.For<IAssistantSettingsStore>(),
            [typeof(ILlmHealth)] = Substitute.For<ILlmHealth>(),
            [typeof(IToolCatalog)] = production,
            [typeof(IEvalToolCatalog)] = evalCatalog,
            [typeof(INightlyRunProbe)] = Substitute.For<INightlyRunProbe>(),
            [typeof(IConversationStore)] = Substitute.For<IConversationStore>(),
            [typeof(IChatModel)] = Substitute.For<IChatModel>(),
            [typeof(TurnGate)] = new TurnGate(),
            [typeof(TimeProvider)] = TimeProvider.System,
            [typeof(XDrishti.Application.Ai.Drafting.DraftProposalTool)] = new XDrishti.Application.Ai.Drafting.DraftProposalTool(Substitute.For<IQueryHandler<XDrishti.Application.Ai.Drafting.SubmitDraftQuery, XDrishti.Application.Ai.Drafting.DraftEvaluation>>(), Substitute.For<IDraftStore>(), Substitute.For<IDraftSheetSource>(), new XDrishti.Application.Ai.Drafting.DraftOptions(), TimeProvider.System),
            [typeof(Microsoft.Extensions.Logging.ILogger<AssistantTurnService>)] = NullLogger<AssistantTurnService>.Instance,
        });

        var runner = EvalTurnRunnerFactory.Create(provider);

        runner.ShouldBeOfType<AssistantTurnService>();
        var fields = typeof(AssistantTurnService).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).ToDictionary(p => p.Name, p => p.GetValue(runner));
        fields["Catalog"].ShouldBeSameAs(evalCatalog);
        fields["Catalog"].ShouldNotBeSameAs(production);
        var options = (TurnOptions)fields["Options"]!;
        (options.Temperature, options.Seed, options.HistoryMessages).ShouldBe((0.0, EvalTurnRunnerFactory.Seed, 0));
    }

    [Fact]
    public async Task The_assistant_status_takes_its_evaluation_from_the_eval_status_and_says_not_yet_evaluated_without_one()
    {
        var settings = Substitute.For<IAssistantSettingsStore>();
        settings.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(AssistantSettings.Defaults));
        var llm = Substitute.For<IQueryHandler<GetLlmStatusQuery, LlmStatusView>>();
        llm.HandleAsync(Arg.Any<GetLlmStatusQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(new LlmStatusView("down", null, null, null, null, DateTimeOffset.UnixEpoch, "x", "y")));
        var nightly = Substitute.For<INightlyRunProbe>();
        var eval = Substitute.For<IQueryHandler<GetEvalStatusQuery, EvalStatusView>>();
        var at = new DateTimeOffset(2026, 10, 28, 1, 2, 3, TimeSpan.Zero);
        eval.HandleAsync(Arg.Any<GetEvalStatusQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(new EvalStatusView(Guid.NewGuid(), "f", "g", false, [], "passed", at, "fine", null, null)));

        var with = await new GetAssistantStatusHandler(settings, llm, Substitute.For<IToolCatalog>(), nightly, new TurnGate(), new TurnOptions(), eval).HandleAsync(new GetAssistantStatusQuery(), Ct);
        var without = await new GetAssistantStatusHandler(settings, llm, Substitute.For<IToolCatalog>(), nightly, new TurnGate(), new TurnOptions()).HandleAsync(new GetAssistantStatusQuery(), Ct);

        with.Evaluation.ShouldBe(new EvaluationView("passed", at, "fine"));
        without.Evaluation.ShouldBe(new EvaluationView("not_yet_evaluated", null, null));
    }

    private sealed class Services(Dictionary<Type, object> map) : IServiceProvider
    {
        public object? GetService(Type serviceType) => map.GetValueOrDefault(serviceType);
    }

    [Fact]
    public void The_drafting_tool_is_never_offered_to_an_eval_turn_even_with_drafting_on_and_an_owner_like_actor()
    {
        var tool = new XDrishti.Application.Ai.Drafting.DraftProposalTool(Substitute.For<IQueryHandler<XDrishti.Application.Ai.Drafting.SubmitDraftQuery, XDrishti.Application.Ai.Drafting.DraftEvaluation>>(), Substitute.For<IDraftStore>(), Substitute.For<IDraftSheetSource>(), new XDrishti.Application.Ai.Drafting.DraftOptions { Enabled = true }, TimeProvider.System);
        var settings = new AssistantSettings(7, true, false, 6);

        tool.OfferedTo(new TurnRequest("q", null, TurnRequest.Eval, "eval:abc"), settings).ShouldBeFalse();
        tool.OfferedTo(new TurnRequest("q", null, TurnRequest.Eval, "owner:owner@example.invalid"), settings).ShouldBeFalse();
        tool.OfferedTo(new TurnRequest("q", null, TurnRequest.Chat, "owner:owner@example.invalid"), settings).ShouldBeTrue("a chat turn of the owner is offered it, so the test proves the origin is what decides");
    }

    private static StoredToolCall Stored(long id, Guid conversation, string? result) =>
        new(id, conversation, Guid.NewGuid(), 1, "run_sql", JsonDocument.Parse("{}").RootElement, "ok", null, result is null ? null : JsonDocument.Parse(result).RootElement, 8, 1, false, null, 0, 0, DateTimeOffset.UnixEpoch, 1);
}
