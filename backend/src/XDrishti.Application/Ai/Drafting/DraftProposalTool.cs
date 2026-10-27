using System.Text.Json;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>
/// <c>lab_propose_run</c> (spec P9-07, P9-00 D9): the chat model's one drafting tool and a PROPOSAL tool. It runs inside the API (the tool server has no write code), takes the YAML and the explanation fields only (the idea is NOT an argument: the server stores the conversation's latest user
/// message verbatim), hands them to <see cref="SubmitDraftHandler"/> (the validator and the checks) and stores one insert-only draft row with <c>via = 'chat_tool'</c>. It answers <c>{draftId, status, problems, findings}</c> so the model can repair within its call budget. It never stores a
/// strategy version, starts a Lab run or confirms: that is the owner's confirm only, and a chat-tool draft is never an auto candidate. It is offered only in the owner's own chat turns while drafting is on.
/// </summary>
internal sealed class DraftProposalTool(IQueryHandler<SubmitDraftQuery, DraftEvaluation> submit, IDraftStore store, IDraftSheetSource sheet, DraftOptions options, TimeProvider time)
{
    public const string Name = "lab_propose_run";

    public static readonly ToolDescriptor Descriptor = new(
        Name,
        "Proposes a strategy draft for the owner to read. Give the strategy YAML and the plain explanation fields; the idea is taken from the owner's message. Validates it, stores a draft (never a strategy, never a run) and answers the problems to fix. The owner confirms or discards.",
        """{"type":"object","additionalProperties":false,"required":["yaml"],"properties":{"decline":{"type":"boolean"},"unsupported":{"type":"array","maxItems":12,"items":{"type":"string"}},"understood":{"type":"array","maxItems":8,"items":{"type":"string"}},"assumptions":{"type":"array","maxItems":12,"items":{"type":"object","required":["field","value","why"],"properties":{"field":{"type":"string"},"value":{"type":"string"},"why":{"type":"string"}}}},"not_expressed":{"type":"array","maxItems":12,"items":{"type":"object","required":["what","why"],"properties":{"what":{"type":"string"},"why":{"type":"string"}}}},"requested_test":{"type":"object","properties":{"from":{"type":["string","null"]},"to":{"type":["string","null"]},"baskets":{"type":"array","items":{"type":"string"}}}},"yaml":{"type":["string","null"]}}}""");

    /// <summary>Whether the tool is offered in this turn: drafting is on, the assistant is on, and the turn is the owner's own chat turn (never an eval, a job or another actor).</summary>
    public bool OfferedTo(TurnRequest request, AssistantSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        return options.Enabled && settings.Enabled && request.Origin == TurnRequest.Chat && OwnerConfirmation.IsOwnerActor(request.Actor);
    }

    /// <summary>Runs the call; the text is the envelope the model reads.</summary>
    public async ValueTask<string> RunAsync(string argumentsJson, TurnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = DraftAnswerReader.Read(argumentsJson);
        if (read.IsFailure)
        {
            return Failure("draft.answer_shape", read.Error.Message);
        }

        var now = time.GetUtcNow();
        var start = new DateTimeOffset(IndiaTime.Today(now).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        if (await store.CountBetweenAsync(start, start.AddDays(1), cancellationToken) >= options.MaxPerDay)
        {
            return Failure(DraftErrors.LimitCode, DraftErrors.Limit(options.MaxPerDay).Message);
        }

        var idea = request.Question.Trim();
        var evaluation = await submit.HandleAsync(new SubmitDraftQuery(idea, read.Value), cancellationToken);
        var id = await store.ReserveIdAsync(cancellationToken);
        var attempt = new DraftAttempt(1, DraftAnswerReader.Clean(argumentsJson.Length > 4000 ? argumentsJson[..4000] : argumentsJson), evaluation.Yaml, evaluation.Problems, evaluation.Findings, 0, null, null);
        await store.InsertAsync(
            new NewDraft(id, idea, evaluation.Status, null, "chat_tool", null, evaluation.Yaml, read.Value, [attempt], evaluation.Problems, evaluation.Findings, null, null, "chat_tool." + PromptRegistry.Draft.Version, sheet.Hash, JsonSerializer.Serialize(new { via = "chat_tool" }), request.Actor),
            CancellationToken.None);
        var problems = evaluation.Problems.Where(p => p.Severity == "error").Take(15).Select(p => new { path = DraftAnswerReader.Clean(p.Path), code = DraftAnswerReader.Clean(p.Code), message = DraftAnswerReader.Clean(p.Message) });
        var findings = evaluation.Findings.Take(15).Select(f => new { code = f.Code, detail = DraftAnswerReader.Clean(f.Detail) });
        return JsonSerializer.Serialize(new
        {
            ok = true,
            tool = Name,
            rows = new[] { new { draftId = id, status = evaluation.Status, problems, findings } },
            rowCount = 1,
            truncated = false,
        });
    }

    private static string Failure(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, tool = Name, rows = (object?)null, data = (object?)null, rowCount = 0, truncated = false, error = new { code, message } });
}
