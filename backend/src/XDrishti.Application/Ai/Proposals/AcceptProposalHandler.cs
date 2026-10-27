using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Proposals;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>
/// The owner's accept (spec P9-08 AC 9, D6). The endpoint has verified the fresh password; this handler refuses any actor that is not the signed-in owner and the capability <c>allow_proposal_accept</c> while it is off. Under the one decision lock it reads the proposal, runs every engine
/// check again against today's data (the world may have changed: <c>assistant.proposal_stale</c>), applies it through <see cref="ProposalApplier"/> and writes the one decision row. A second accept returns the first result and changes nothing; a rejected or expired proposal is closed.
/// Accepting writes a proposed hypothesis or changes a basket. It never tracks an instrument, starts a backfill, approves, runs or applies anything, and places no order.
/// </summary>
internal sealed class AcceptProposalHandler(
    IProposalReader reader,
    IProposalDecisions decisions,
    IAssistantSettingsStore settings,
    ProposalEvaluator evaluator,
    ProposalApplier applier,
    TimeProvider time,
    ILogger<AcceptProposalHandler> logger) : ICommandHandler<AcceptProposalCommand, ProposalView>
{
    public async ValueTask<Result<ProposalView>> HandleAsync(AcceptProposalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Refuse(ProposalErrors.OwnerOnly());
        }

        if (!(await settings.CurrentAsync(cancellationToken)).AllowProposalAccept)
        {
            return Refuse(ProposalErrors.CapabilityOff());
        }

        await using var decisionLock = await decisions.LockAsync(cancellationToken);

        // The switch is read again under the lock: an owner who switched accepting off while this waited must not get one stale accept.
        if (!(await settings.CurrentAsync(cancellationToken)).AllowProposalAccept)
        {
            return Refuse(ProposalErrors.CapabilityOff());
        }

        var now = time.GetUtcNow();
        if (await reader.GetAsync(command.Id, cancellationToken) is not { } proposal)
        {
            return Refuse(ProposalErrors.NotFound(command.Id));
        }

        if (proposal.Decision is { } earlier)
        {
            return earlier.Decision == ProposalLifecycle.Accepted ? Result.Success(ProposalView.Of(proposal, now)) : Refuse(ProposalErrors.Closed(earlier.Decision));
        }

        if (proposal.ExpiresAt <= now)
        {
            return Refuse(ProposalErrors.Closed(ProposalLifecycle.Expired));
        }

        var payload = ProposalJson.ReadPayload(proposal.Kind, proposal.PayloadJson);
        if (payload is null)
        {
            return Refuse(ProposalErrors.Stale(ProposalCodes.PayloadInvalid, "The stored payload cannot be read."));
        }

        var evaluation = await evaluator.EvaluateAsync(proposal.Kind, payload, ProposalEvaluator.RefsOf(payload), EvaluationMode.Recheck, [], cancellationToken);
        if (!evaluation.Passed)
        {
            var failed = evaluation.Checks.First(c => !c.Ok);
            var refusal = ProposalCodes.Refusal(failed.Code);
            ProposalLog.Stale(logger, proposal.Id, refusal);
            return Refuse(ProposalErrors.Stale(refusal, failed.Detail));
        }

        var applied = await applier.ApplyAsync(proposal.Id, proposal.Kind, payload, proposal.Reason, command.Actor, cancellationToken);
        if (applied.IsFailure)
        {
            return Refuse(applied.Error);
        }

        var written = await ProposalDecisionWriter.WriteAsync(decisions, logger, new NewProposalDecision(proposal.Id, ProposalLifecycle.Accepted, command.Actor, false, null, null, null, applied.Value));
        ProposalLog.Decided(logger, proposal.Id, ProposalLifecycle.Accepted, false);
        return Result.Success(ProposalView.Of(proposal with { Decision = written.Decision }, now));
    }

    private Result<ProposalView> Refuse(Error error)
    {
        ProposalLog.Refused(logger, error.Code);
        return Result.Failure<ProposalView>(error);
    }
}

/// <summary>The owner rejects a proposal (spec P9-08 AC 10): one decision row, and the repeat block starts. Free of the password and of the capability (the safe direction). A decided or expired proposal cannot be rejected; a second reject returns the first result.</summary>
internal sealed class RejectProposalHandler(IProposalReader reader, IProposalDecisions decisions, TimeProvider time, ILogger<RejectProposalHandler> logger) : ICommandHandler<RejectProposalCommand, ProposalView>
{
    public async ValueTask<Result<ProposalView>> HandleAsync(RejectProposalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Refuse(ProposalErrors.OwnerOnly());
        }

        var reason = command.Reason is null ? null : XDrishti.Domain.Assistant.UntrustedText.Clean(command.Reason).Text.Trim();
        if (reason is { Length: > ProposalErrors.MaxRejectReason })
        {
            return Refuse(ProposalErrors.Invalid($"The reason is at most {ProposalErrors.MaxRejectReason} characters."));
        }

        await using var decisionLock = await decisions.LockAsync(cancellationToken);
        var now = time.GetUtcNow();
        if (await reader.GetAsync(command.Id, cancellationToken) is not { } proposal)
        {
            return Refuse(ProposalErrors.NotFound(command.Id));
        }

        if (proposal.Decision is { } earlier)
        {
            return earlier.Decision == ProposalLifecycle.Rejected ? Result.Success(ProposalView.Of(proposal, now)) : Refuse(ProposalErrors.Closed(earlier.Decision));
        }

        if (proposal.ExpiresAt <= now)
        {
            return Refuse(ProposalErrors.Closed(ProposalLifecycle.Expired));
        }

        var written = await decisions.InsertAsync(new NewProposalDecision(proposal.Id, ProposalLifecycle.Rejected, command.Actor, false, null, null, string.IsNullOrEmpty(reason) ? null : reason, null), cancellationToken);
        if (written.Created)
        {
            ProposalLog.Decided(logger, proposal.Id, ProposalLifecycle.Rejected, false);
        }

        return Result.Success(ProposalView.Of(proposal with { Decision = written.Decision }, now));
    }

    private Result<ProposalView> Refuse(Error error)
    {
        ProposalLog.Refused(logger, error.Code);
        return Result.Failure<ProposalView>(error);
    }
}
