using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Assistant.Evaluation;

namespace XDrishti.Application.Ai;

/// <summary>
/// The status of the assistant (spec P9-03 AC 9): the settings in force, the model server's state, the tool server's state (not asked while the assistant is off: nothing talks to anything then), the single turn slot, the nightly guard and the evaluation.
/// </summary>
internal sealed class GetAssistantStatusHandler(
    IAssistantSettingsStore settingsStore,
    IQueryHandler<GetLlmStatusQuery, LlmStatusView> llmStatus,
    IToolCatalog tools,
    INightlyRunProbe nightly,
    TurnGate gate,
    TurnOptions options,
    IQueryHandler<GetEvalStatusQuery, EvalStatusView>? evaluation = null) : IQueryHandler<GetAssistantStatusQuery, AssistantStatusView>
{
    public async ValueTask<AssistantStatusView> HandleAsync(GetAssistantStatusQuery query, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.CurrentAsync(cancellationToken);
        var llm = await llmStatus.HandleAsync(new GetLlmStatusQuery(), cancellationToken);
        var toolsView = new ToolsStatusView("disabled", 0, 0);
        if (settings.Enabled)
        {
            var snapshot = await tools.ListAsync(cancellationToken);
            toolsView = new ToolsStatusView(snapshot.State.ToString().ToLowerInvariant(), snapshot.Tools.Count, ContextBudget.SchemaTokens(snapshot.Tools));
        }

        var running = await nightly.IsRunningAsync(TimeSpan.FromMinutes(options.NightlyGuardMinutes), cancellationToken);
        var eval = evaluation is null ? null : await evaluation.HandleAsync(new GetEvalStatusQuery(), cancellationToken);
        return new AssistantStatusView(settings.Enabled, settings.PortfolioContext, settings.MaxToolCalls, settings.Id, llm, toolsView, gate.Busy, running, eval is null ? new EvaluationView("not_yet_evaluated", null, null) : new EvaluationView(eval.State, eval.At, eval.Detail), settings.AllowDraftConfirm, settings.AutoLabQuickTest, settings.AllowProposalAccept, settings.AutoAcceptHypothesis);
    }
}
