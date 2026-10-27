using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>
/// The dedicated drafting job (spec P9-07 D2): "JSON in, JSON out" with no tools. One model call, then at most <c>MaxRepairs</c> repair turns that carry only the located problems and the failing text. Every answer goes to the same core as the chat tool (<see cref="SubmitDraftHandler"/>).
/// The only write is the insert-only draft row, once, when the job ends; it never stores a strategy and never starts a Lab run. The model is down or not ready, the answer does not fit the schema, or the job times out: each is an honest <c>failed</c> state with its reason.
/// </summary>
internal sealed class DraftStrategyHandler(
    IDraftStore store,
    IDraftSheetSource sheet,
    IStrategyDrafter drafter,
    IQueryHandler<SubmitDraftQuery, DraftEvaluation> submit,
    ILlmHealth health,
    IModelCallAudit audit,
    ICommandHandler<AutoDraftCommand, AutoDraftOutcome> auto,
    DraftOptions options,
    ILogger<DraftStrategyHandler> logger) : ICommandHandler<DraftStrategyCommand, DraftView>
{
    public const string ReasonModelUnavailable = "model_unavailable";
    public const string ReasonTimeout = "timeout";
    public const string ReasonAnswerShape = "answer_shape";

    private const int MaxOutput = 1800;

    public async ValueTask<Result<DraftView>> HandleAsync(DraftStrategyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(options.JobTimeoutSeconds));
        Outcome outcome;
        try
        {
            outcome = await RunAsync(command, limit.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = Outcome.Fail(ReasonTimeout, []);
        }

        var draft = new NewDraft(
            command.Id, command.Idea, outcome.Status, outcome.Reason, "job", command.BasketId, outcome.Yaml, outcome.Answer, outcome.Attempts, outcome.Problems, outcome.Findings, outcome.ModelId, outcome.ModelSha256,
            "draft." + PromptRegistry.Draft.Version, sheet.Hash, ParamsJson(), command.Actor);
        await store.InsertAsync(draft, CancellationToken.None);
        DraftLog.Finished(logger, command.Id, outcome.Status, outcome.Reason, outcome.Attempts.Count);
        if (outcome.Status == DraftStatus.Valid)
        {
            // Off unless the owner switched it on; every condition it needs is checked there, and a draft that fails one stays for the owner.
            await auto.HandleAsync(new AutoDraftCommand(command.Id), CancellationToken.None);
        }

        var stored = await store.GetAsync(command.Id, CancellationToken.None);
        return stored is null ? Result.Failure<DraftView>(DraftErrors.NotFound(command.Id)) : Result.Success(DraftView.Of(stored, null, null));
    }

    private async ValueTask<Outcome> RunAsync(DraftStrategyCommand command, CancellationToken cancellationToken)
    {
        var status = await health.GetStatusAsync(cancellationToken);
        if (status.State != LlmState.Ready)
        {
            return Outcome.Fail(ReasonModelUnavailable, []);
        }

        var modelId = status.ModelFile ?? status.Model ?? "unknown";
        var system = DraftPrompt.System(sheet.Sheet);
        var promptHash = PromptRegistry.Draft.Sha256;
        var attempts = new List<DraftAttempt>();
        var user = DraftPrompt.First(command.Idea);
        DraftAnswer? answer = null;
        DraftEvaluation? last = null;
        var lastShapeOk = true;
        for (var n = 1; n <= 1 + options.MaxRepairs; n++)
        {
            DraftModelAnswer given;
            try
            {
                given = await drafter.DraftAsync(new DraftModelRequest(system, user, MaxOutput, options.Seed), cancellationToken);
            }
            catch (ModelUnavailableException)
            {
                DraftLog.ModelFailed(logger, command.Id, n);
                await audit.RecordAsync(new ModelCallRecord("draft", command.Actor, promptHash, modelId, status.ModelSha256, null, null, 0, "error", "error", "model_unavailable"), CancellationToken.None);
                return Outcome.Fail(ReasonModelUnavailable, attempts, modelId, status.ModelSha256);
            }

            await audit.RecordAsync(new ModelCallRecord("draft", command.Actor, promptHash, modelId, status.ModelSha256, given.PromptTokens, given.CompletionTokens, given.LatencyMs, given.FinishReason, "ok", null), CancellationToken.None);
            var read = given.FinishReason == "length" ? Result.Failure<DraftAnswer>(Error.Validation(DraftCodes.AnswerShape, "The answer was cut.")) : DraftAnswerReader.Read(given.Text);
            lastShapeOk = read.IsSuccess;
            if (read.IsFailure)
            {
                var finding = new DraftFinding(DraftCodes.AnswerShape, string.Empty);
                attempts.Add(new DraftAttempt(n, given.Text, null, [], [finding], given.LatencyMs, given.PromptTokens, given.CompletionTokens));
                DraftLog.Attempt(logger, command.Id, n, "unreadable", 0, 1, DraftCodes.AnswerShape, given.LatencyMs);
                user = DraftPrompt.Repair(command.Idea, null, [], [finding]);
                continue;
            }

            answer = read.Value;
            last = await submit.HandleAsync(new SubmitDraftQuery(command.Idea, answer), cancellationToken);
            attempts.Add(new DraftAttempt(n, given.Text, last.Yaml, last.Problems, last.Findings, given.LatencyMs, given.PromptTokens, given.CompletionTokens));
            var errors = last.Problems.Count(p => p.Severity == "error");
            var codes = string.Join(',', last.Findings.Select(f => f.Code).Distinct(StringComparer.Ordinal));
            DraftLog.Attempt(logger, command.Id, n, last.Status, errors, last.Findings.Count, codes, given.LatencyMs);
            if (!last.NeedsRepair)
            {
                break;
            }

            user = DraftPrompt.Repair(command.Idea, last.Yaml, last.Problems, last.Findings);
        }

        if (!lastShapeOk || last is null)
        {
            return Outcome.Fail(ReasonAnswerShape, attempts, modelId, status.ModelSha256, answer);
        }

        return new Outcome(last.Status, null, last.Yaml, answer, attempts, last.Problems, last.Findings, modelId, status.ModelSha256);
    }

    private string ParamsJson() => JsonSerializer.Serialize(new { temperature = 0, seed = options.Seed, maxOutputTokens = MaxOutput, maxRepairs = options.MaxRepairs, jobTimeoutSeconds = options.JobTimeoutSeconds, thinking = "the model server's setting", format = "json object; schema re-validated on our side" });

    private sealed record Outcome(string Status, string? Reason, string? Yaml, DraftAnswer? Answer, IReadOnlyList<DraftAttempt> Attempts, IReadOnlyList<StrategyIssue> Problems, IReadOnlyList<DraftFinding> Findings, string? ModelId, string? ModelSha256)
    {
        public static Outcome Fail(string reason, IReadOnlyList<DraftAttempt> attempts, string? modelId = null, string? sha = null, DraftAnswer? answer = null) =>
            new(DraftStatus.Failed, reason, null, answer, attempts, [], [], modelId, sha);
    }
}
