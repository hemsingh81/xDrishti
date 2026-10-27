using System.Diagnostics;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Asks the assistant one question as <c>Origin: eval</c> (spec P9-09 AC 8, 13, ADR 0143) through the one turn runner, built over the eval tool catalog and options fixed for the run (temperature 0, a seed): the tools read the fixture only; the turn is audited like every
/// turn (conversation origin <c>eval</c>), and the tool results it read are fetched back from that audit for grounding. The turn runner honours the master switch and every refusal; the eval origin is never offered the drafting tool.
/// </summary>
internal sealed class EvalTurnAnswerSource(IAssistantTurnRunner runner, IConversationReader conversations) : IEvalAnswerSource
{
    public async ValueTask<Result<EvalAnswer>> AskAsync(string question, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        var clock = Stopwatch.StartNew();
        Guid? conversation = null;
        var calls = new List<long>();
        var begun = await runner.BeginAsync(new TurnRequest(question, null, TurnRequest.Eval, actor), cancellationToken);
        if (begun.IsFailure)
        {
            return Result.Failure<EvalAnswer>(begun.Error);
        }

        await using var session = begun.Value;
        var result = await session.RunAsync(
            e =>
            {
                switch (e)
                {
                    case TurnStarted started:
                        conversation = started.ConversationId;
                        break;
                    case TurnToolResult tool:
                        calls.Add(tool.ToolCallId);
                        break;
                    default:
                        break;
                }

                return ValueTask.CompletedTask;
            },
            cancellationToken);

        if (result.FinishReason is "error")
        {
            return Result.Failure<EvalAnswer>(new Error("assistant.model_unavailable", "The model did not answer.", ErrorKind.Upstream));
        }

        var results = new List<string>();
        foreach (var id in calls)
        {
            var stored = await conversations.GetToolCallAsync(id, cancellationToken);
            if (stored?.Result is { } json)
            {
                results.Add(json.GetRawText());
            }
        }

        return Result.Success(new EvalAnswer(result.Answer, conversation, results, result.ToolCalls, (int)clock.ElapsedMilliseconds, result.Usage.PromptTokens, result.Usage.CompletionTokens, result.FinishReason));
    }
}
