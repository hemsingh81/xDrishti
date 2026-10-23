using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Autonomy;

/// <summary>
/// One pass of the auto-approval rule over a plan (spec P7-12, ADR 0091), to be called by the cutoff path strictly BEFORE the review cutoff. The first thing it reads is the switch: OFF (or never stored) returns at once with nothing else read or
/// written, so with the default the plan record behaves exactly as before. ON: for each primary Proposed ticket in rank order it asks <see cref="AutoApprovalPolicy"/>; an approval is written through <see cref="IAutoApprovalStore"/>
/// (rule actor, settings snapshot, policy version) and the database re-checks the switch, primary, cutoff and cap. A decline leaves the ticket alone: the owner approves by hand, or it lapses at the cutoff. It never moves a plan.
/// </summary>
internal sealed class AutoApprovePlanHandler(
    IAutonomySettingsStore settingsStore,
    IPlanStore plans,
    IAutoApprovalStore autoStore,
    IChecklistSource checklists,
    TimeProvider time,
    ILogger<AutoApprovePlanHandler> logger) : ICommandHandler<AutoApprovePlanCommand, AutoApprovalOutcome>
{
    public async ValueTask<Result<AutoApprovalOutcome>> HandleAsync(AutoApprovePlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var settings = await settingsStore.CurrentAsync(cancellationToken);
        if (!settings.AutoApproveTickets || settings.Id is not { } settingsId || settings.Hash is not { } settingsHash)
        {
            AutonomyLog.SkippedOff(logger, command.PlanId);
            return Result.Success(new AutoApprovalOutcome(command.PlanId, false, 0, []));
        }

        var plan = await plans.FindAsync(command.PlanId, cancellationToken);
        if (plan is null)
        {
            return Refuse(PlanErrors.NotFound(command.PlanId));
        }

        var now = time.GetUtcNow();
        if (plan.State is not (PlanState.Published or PlanState.InReview) || now >= plan.Plan.ReviewCutoff)
        {
            return Refuse(PlanErrors.MoveRefused(["Automatic approval needs a published plan before its review cutoff."]));
        }

        var items = await plans.ItemsAsync(command.PlanId, cancellationToken);
        var today = await autoStore.CountAutoApprovalsAsync(plan.Plan.ForDate, cancellationToken);
        var approved = 0;
        var declined = new List<AutoDeclinedTicket>();
        foreach (var item in items.Where(i => i.Item.Group == TicketGroup.Primary && i.State == TicketState.Proposed).OrderBy(i => i.Item.Rank))
        {
            var evidence = item.Item.Evidence;
            var ticket = new AutoApprovalTicket(item.Id, item.Item.Symbol, item.Item.Group, item.State, evidence.PWinLower, evidence.Unavailable, [.. item.Item.Allocations.Select(a => a.AccountId.ToString())]);
            var results = await checklists.ForTicketAsync(item, cancellationToken);
            var decision = AutoApprovalPolicy.Decide(ticket, results, settings, new AutoApprovalContext(now, plan.Plan.ReviewCutoff, today));
            if (!decision.Approved)
            {
                declined.Add(new AutoDeclinedTicket(item.Id, [.. decision.Reasons.Select(r => r.Code)]));
                continue;
            }

            var status = await autoStore.AutoApproveAsync(new AutoApproveMove(item.Id, settingsId, settingsHash, decision.PolicyVersion, decision.Summary), cancellationToken);
            if (status == MoveStatus.Created)
            {
                approved++;
                today++;
                AutonomyLog.Approved(logger, item.Id, settingsId);
            }
            else
            {
                // The owner decided first, or the database refused: either way the ticket stays as the other path left it.
                declined.Add(new AutoDeclinedTicket(item.Id, ["store_" + status.ToString().ToLowerInvariant()]));
            }
        }

        AutonomyLog.Pass(logger, command.PlanId, approved, declined.Count);
        return Result.Success(new AutoApprovalOutcome(command.PlanId, true, approved, declined));
    }

    private Result<AutoApprovalOutcome> Refuse(Error error)
    {
        AutonomyLog.Refused(logger, error.Code);
        return Result.Failure<AutoApprovalOutcome>(error);
    }
}
