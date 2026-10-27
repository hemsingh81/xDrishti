using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Proposals;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>
/// The proposal job (spec P9-08 AC 1 to 8, 12, 13, D1): builds the pack from the engine, asks the model once (and once more if the answer does not read), and lets the engine decide each item: every reference is read again, every rule is checked and the reason is checked against the
/// evidence. Only an item that passes is stored, as an insert-only inbox row. This handler holds no decision port and no hypothesis or basket command: the model's only write is the inbox. A model that is off, down or busy, an answer that does not read, or every item refused: nothing is stored
/// and nothing else changes. With the auto option on, the hypotheses just stored are offered to the auto handler, which re-checks them.
/// </summary>
internal sealed class GenerateProposalsHandler(
    ProposalPackBuilder packs,
    ProposalEvaluator evaluator,
    IProposalModel model,
    IProposalInbox inbox,
    IProposalReader reader,
    IAssistantSettingsStore settingsStore,
    ILlmHealth health,
    IModelCallAudit audit,
    ICommandHandler<AutoAcceptProposalsCommand, AutoAcceptOutcome> auto,
    TurnGate gate,
    ProposalOptions options,
    TimeProvider time,
    ILogger<GenerateProposalsHandler> logger) : ICommandHandler<GenerateProposalsCommand, ProposalRunView>
{
    private const int MaxOutput = 1200;
    private const int Attempts = 2;

    public async ValueTask<Result<ProposalRunView>> HandleAsync(GenerateProposalsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Kind is not null && !ProposalKind.IsKnown(command.Kind))
        {
            return Result.Failure<ProposalRunView>(ProposalErrors.Invalid("The kind is hypothesis, basket_add or basket_remove."));
        }

        if (!options.Enabled || !(await settingsStore.CurrentAsync(cancellationToken)).Enabled)
        {
            ProposalLog.Refused(logger, ProposalErrors.DisabledCode);
            return Result.Failure<ProposalRunView>(ProposalErrors.Disabled());
        }

        var pack = await packs.BuildAsync(command.Kind, command.Cell, cancellationToken);
        ProposalLog.Pack(logger, pack.Cells, pack.Baskets, pack.Candidates, pack.Unavailable.Count, pack.Hash);
        if (pack.Empty)
        {
            return Done(command, pack, "nothing_to_propose", pack.Unavailable.Count > 0 ? "inputs_unavailable" : null, 0, 0, [], [], 0);
        }

        using var slot = gate.TryEnter();
        if (slot is null)
        {
            ProposalLog.Refused(logger, ProposalErrors.BusyCode);
            return Result.Failure<ProposalRunView>(ProposalErrors.Busy());
        }

        var status = await health.GetStatusAsync(cancellationToken);
        if (status.State != LlmState.Ready)
        {
            return Done(command, pack, "model_unavailable", null, 0, 0, [], [], 0);
        }

        var modelId = status.ModelFile ?? status.Model ?? "unknown";
        var answer = await AskAsync(command, pack, status, modelId, cancellationToken);
        if (answer.State is not null)
        {
            return Done(command, pack, answer.State, null, 0, 0, [], [], 0);
        }

        var now = time.GetUtcNow();
        var pending = (await reader.PendingAsync(now, cancellationToken)).Select(PendingOf).ToList();
        var refused = answer.Answer!.Refused.Select(code => new ProposalRefusedView("unknown", code)).ToList();
        var previews = new List<string>();
        var storedHypotheses = new List<long>();
        var stored = 0;
        foreach (var item in answer.Answer.Items)
        {
            if (command.Kind is not null && item.Kind != command.Kind)
            {
                refused.Add(new ProposalRefusedView(item.Kind, "kind_not_asked"));
                continue;
            }

            if (stored >= options.MaxPerRun)
            {
                refused.Add(new ProposalRefusedView(item.Kind, "run_limit"));
                continue;
            }

            if (pending.Count >= options.MaxPending)
            {
                refused.Add(new ProposalRefusedView(item.Kind, ProposalCodes.PendingLimit));
                continue;
            }

            var evaluation = await evaluator.EvaluateAsync(item.Kind, item.Payload, item.EvidenceRefs, EvaluationMode.Generate, pending, cancellationToken);
            if (!evaluation.Passed)
            {
                refused.Add(new ProposalRefusedView(item.Kind, ProposalChecks.FirstRefusal(evaluation.Checks) ?? "check_failed"));
                continue;
            }

            if (ProposalReasonGuard.Check(item.Reason, evaluation.Subject, evaluation.Evidence) is { Count: > 0 } findings)
            {
                refused.Add(new ProposalRefusedView(item.Kind, findings[0]));
                continue;
            }

            if (command.DryRun)
            {
                previews.Add(evaluation.Subject);
                stored++;
                pending.Add(PendingOfItem(item, evaluation.Key));
                continue;
            }

            var row = new NewProposal(
                item.Kind, evaluation.Key, IndiaTime.Today(now), evaluation.Subject, ProposalJson.Payload(item.Payload), item.Reason, ProposalJson.Evidence(evaluation.Evidence), ProposalJson.Checks(evaluation.Checks), pack.Hash, modelId, status.ModelSha256,
                "proposal." + PromptRegistry.Proposal.Version, command.Actor, ProposalLifecycle.ExpiryOf(item.Kind, now, options.HypothesisExpiryDays, options.BasketExpiryDays));
            if (await inbox.InsertAsync(row, cancellationToken) is not { } id)
            {
                refused.Add(new ProposalRefusedView(item.Kind, ProposalCodes.DuplicatePending));
                continue;
            }

            stored++;
            ProposalLog.Stored(logger, id, item.Kind);
            pending.Add(PendingOfItem(item, evaluation.Key));
            if (item.Kind == ProposalKind.Hypothesis)
            {
                storedHypotheses.Add(id);
            }
        }

        var accepted = 0;
        if (storedHypotheses.Count > 0)
        {
            // Off unless the owner switched it on; every condition it needs is checked there, and a proposal that fails one stays pending for the owner.
            var outcome = await auto.HandleAsync(new AutoAcceptProposalsCommand(storedHypotheses), CancellationToken.None);
            accepted = outcome.IsSuccess ? outcome.Value.Accepted : 0;
        }

        return Done(command, pack, stored > 0 ? "stored" : "nothing_stored", null, answer.Answer.Items.Count + answer.Answer.Refused.Count, stored, refused, previews, accepted);
    }

    private static PendingItem PendingOf(StoredProposal p)
    {
        var payload = ProposalJson.ReadPayload(p.Kind, p.PayloadJson);
        return payload is HypothesisPayload h ? new PendingItem(p.Kind, p.DedupeKey, h.CellKey, h.KnobName) : new PendingItem(p.Kind, p.DedupeKey, null, null);
    }

    private static PendingItem PendingOfItem(ProposalItem item, string key) =>
        item.Payload is HypothesisPayload h ? new PendingItem(item.Kind, key, h.CellKey, h.KnobName) : new PendingItem(item.Kind, key, null, null);

    private async ValueTask<(ProposalAnswer? Answer, string? State)> AskAsync(GenerateProposalsCommand command, ProposalPack pack, LlmStatus status, string modelId, CancellationToken cancellationToken)
    {
        var user = ProposalPrompt.User(pack.Text);
        for (var n = 1; n <= Attempts; n++)
        {
            ProposalModelAnswer given;
            try
            {
                given = await model.ProposeAsync(new ProposalModelRequest(PromptRegistry.Proposal.Text, user, MaxOutput, options.Seed), cancellationToken);
            }
            catch (ModelUnavailableException)
            {
                ProposalLog.ModelFailed(logger, n);
                await audit.RecordAsync(new ModelCallRecord("proposal", command.Actor, PromptRegistry.Proposal.Sha256, modelId, status.ModelSha256, null, null, 0, "error", "error", "model_unavailable"), CancellationToken.None);
                return (null, "model_unavailable");
            }

            await audit.RecordAsync(new ModelCallRecord("proposal", command.Actor, PromptRegistry.Proposal.Sha256, modelId, status.ModelSha256, given.PromptTokens, given.CompletionTokens, given.LatencyMs, given.FinishReason, "ok", null), CancellationToken.None);
            var read = given.FinishReason == "length" ? Result.Failure<ProposalAnswer>(Error.Validation(ProposalCodes.AnswerShape, "The answer was cut.")) : ProposalAnswerReader.Read(given.Text);
            ProposalLog.Attempt(logger, n, read.IsSuccess ? read.Value.Items.Count : 0, read.IsSuccess ? read.Value.Refused.Count : 0, given.LatencyMs);
            if (read.IsSuccess)
            {
                return (read.Value, null);
            }

            user = ProposalPrompt.Retry(pack.Text);
        }

        return (null, "answer_shape");
    }

    private Result<ProposalRunView> Done(GenerateProposalsCommand command, ProposalPack pack, string state, string? reason, int read, int stored, List<ProposalRefusedView> refused, List<string> previews, int accepted)
    {
        var codes = string.Join(',', refused.Select(r => r.Code).Distinct(StringComparer.Ordinal));
        ProposalLog.Run(logger, state, reason, stored, refused.Count, codes);
        return Result.Success(new ProposalRunView(state, reason, pack.Cells, pack.Candidates, read, stored, accepted, refused, previews, pack.Hash, command.DryRun));
    }
}
