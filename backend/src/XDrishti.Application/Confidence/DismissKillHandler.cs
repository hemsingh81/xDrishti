using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The owner's dismissal of a kill proposal (spec CF-12 leftover, ADR 0234): one conditional update from <c>proposed</c> to <c>dismissed</c> that records the owner and the reason. It refuses a non-owner actor and a missing or over-long reason before anything is read. It touches no
/// lifecycle state (no cell is paused, re-enabled or changed) and the automatic setting is not consulted, so nothing it does lowers or raises risk. Dismissing twice changes nothing twice; an applied proposal cannot be dismissed. No order is placed.
/// </summary>
internal sealed class DismissKillHandler(IKillProposalReader reader, IKillProposalStore store, ILogger<DismissKillHandler> logger) : ICommandHandler<DismissKillCommand, DismissKillOutcome>
{
    private const int MaxReason = 400;

    public async ValueTask<Result<DismissKillOutcome>> HandleAsync(DismissKillCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!KillActors.IsOwner(command.Actor))
        {
            KillLog.Refused(logger, KillErrors.OwnerOnlyCode);
            return Result.Failure<DismissKillOutcome>(KillErrors.OwnerOnly());
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReason)
        {
            return Result.Failure<DismissKillOutcome>(KillErrors.ReasonInvalid(MaxReason));
        }

        var row = await reader.FindAsync(command.ProposalId, cancellationToken);
        if (row is null)
        {
            return Result.Failure<DismissKillOutcome>(KillErrors.ProposalNotFound(command.ProposalId));
        }

        if (row.State == "dismissed")
        {
            return Result.Success(Outcome(row, true));
        }

        if (row.State != "proposed")
        {
            KillLog.Refused(logger, KillErrors.ProposalAppliedCode);
            return Result.Failure<DismissKillOutcome>(KillErrors.ProposalApplied(row.Id));
        }

        var decided = await store.DismissAsync(row.Id, command.Actor, reason, cancellationToken);
        if (decided == KillDecideResult.Refused)
        {
            KillLog.Refused(logger, KillErrors.DecisionRefusedCode);
            return Result.Failure<DismissKillOutcome>(KillErrors.DecisionRefused(row.Id));
        }

        if (decided == KillDecideResult.NotProposed)
        {
            // decided by another caller between the read and the update: report what stands
            var now = await reader.FindAsync(row.Id, cancellationToken) ?? row;
            return now.State switch
            {
                "dismissed" => Result.Success(Outcome(now, true)),
                _ => Result.Failure<DismissKillOutcome>(KillErrors.ProposalApplied(row.Id)),
            };
        }

        KillLog.Dismissed(logger, row.Id);
        return Result.Success(Outcome(row with { State = "dismissed", Actor = command.Actor, DismissReason = reason }, false));
    }

    private static DismissKillOutcome Outcome(KillProposalRow row, bool already) =>
        new(row.Id, row.Proposal.Strategy, row.Proposal.Rule, "dismissed", already, row.Actor, row.DismissReason);
}
