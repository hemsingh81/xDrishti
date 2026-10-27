using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Proposals;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>
/// The auto option (spec P9-08 AC 10a, D8; the owner's rule that anything with a manual approval can also run automatically, default OFF). Only hypotheses, and only the ones the generation job has just stored. With the accept handler, this is the only type of the proposal code that
/// writes a hypothesis. It acts only when ALL hold, and every doubt leaves the proposal pending for the owner: <c>Assistant:Proposals:Enabled</c>, <c>ai.settings.enabled</c> and <c>auto_accept_hypothesis</c> are on now (the setting is read again under the decision lock), the proposal is a
/// hypothesis with no decision and not expired, fewer than three were auto-accepted on this India date (counted from the decision rows), and every engine check passes again on today's data (an input that is unavailable is not a pass). It writes the hypothesis <c>proposed</c> only, as actor
/// <c>system:auto-accept</c>, with the decision recorded as auto with the settings row id and hash; it approves, starts and judges nothing, and places no order.
/// </summary>
internal sealed class AutoAcceptProposalsHandler(
    IProposalReader reader,
    IProposalDecisions decisions,
    IAssistantSettingsStore settings,
    ProposalEvaluator evaluator,
    ProposalApplier applier,
    ProposalOptions options,
    TimeProvider time,
    ILogger<AutoAcceptProposalsHandler> logger) : ICommandHandler<AutoAcceptProposalsCommand, AutoAcceptOutcome>
{
    public const string Actor = "system:auto-accept";

    public async ValueTask<Result<AutoAcceptOutcome>> HandleAsync(AutoAcceptProposalsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await OnAsync(cancellationToken))
        {
            return Left(command, "off");
        }

        await using var decisionLock = await decisions.LockAsync(cancellationToken);

        // The switch is read again under the lock: an owner who switched the option off while this waited must not get one stale accept, and the setting row recorded below is the one read here.
        var current = await settings.CurrentAsync(cancellationToken);
        if (!options.Enabled || !current.Enabled || !current.AutoAcceptHypothesis || current.Id is null || current.SnapshotHash is null)
        {
            return Left(command, "off");
        }

        var accepted = 0;
        string? left = null;
        foreach (var id in command.Ids.Distinct())
        {
            var reason = await OneAsync(id, current.Id.Value, current.SnapshotHash, cancellationToken);
            ProposalLog.Auto(logger, id, reason is null, reason ?? "accepted");
            if (reason is null)
            {
                accepted++;
            }
            else
            {
                left ??= reason;
            }
        }

        return Result.Success(new AutoAcceptOutcome(accepted, left));
    }

    private async ValueTask<bool> OnAsync(CancellationToken cancellationToken)
    {
        var current = await settings.CurrentAsync(cancellationToken);
        return options.Enabled && current.Enabled && current.AutoAcceptHypothesis && current.Id is not null && current.SnapshotHash is not null;
    }

    /// <summary>Null when accepted, else the one-word reason the proposal was left to the owner.</summary>
    private async ValueTask<string?> OneAsync(long id, long settingsId, string snapshotHash, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (await reader.GetAsync(id, cancellationToken) is not { } proposal || proposal.Kind != ProposalKind.Hypothesis)
        {
            return "not_a_hypothesis";
        }

        if (proposal.Decision is not null || proposal.ExpiresAt <= now)
        {
            return "closed";
        }

        var dayStart = new DateTimeOffset(IndiaTime.Today(now).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        if (await reader.CountAutoBetweenAsync(dayStart, dayStart.AddDays(1), cancellationToken) >= options.AutoPerDay)
        {
            return "limit_day";
        }

        if (ProposalJson.ReadPayload(proposal.Kind, proposal.PayloadJson) is not { } payload)
        {
            return "payload_unreadable";
        }

        ProposalEvaluation evaluation;
        try
        {
            evaluation = await evaluator.EvaluateAsync(proposal.Kind, payload, ProposalEvaluator.RefsOf(payload), EvaluationMode.Recheck, [], cancellationToken);
        }
#pragma warning disable CA1031 // an input that cannot be read is no pass: the proposal waits for the owner
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return "inputs_unavailable";
        }

        if (!evaluation.Passed)
        {
            return "checks_not_passed";
        }

        var applied = await applier.ApplyAsync(proposal.Id, proposal.Kind, payload, proposal.Reason, Actor, cancellationToken);
        if (applied.IsFailure)
        {
            return "apply_refused";
        }

        await ProposalDecisionWriter.WriteAsync(decisions, logger, new NewProposalDecision(proposal.Id, ProposalLifecycle.Accepted, Actor, true, settingsId, snapshotHash, null, applied.Value));
        ProposalLog.Decided(logger, proposal.Id, ProposalLifecycle.Accepted, true);
        return null;
    }

    private Result<AutoAcceptOutcome> Left(AutoAcceptProposalsCommand command, string reason)
    {
        foreach (var id in command.Ids)
        {
            ProposalLog.Auto(logger, id, false, reason);
        }

        return Result.Success(new AutoAcceptOutcome(0, reason));
    }
}
