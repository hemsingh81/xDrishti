using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// "Approve all within limits" (spec P7-10, ADR 0095). Approves only PRIMARY tickets that are still Proposed and whose stored checklist is complete with EVERY check passed on every account (a warned, failed or unavailable check is never approved here,
/// and no confirmation is accepted: approving despite a check is a per-ticket act). Reserve, modified and decided tickets are never touched. Each ticket is its own transaction through <see cref="MoveTicketCommand"/>, so the owner path, the
/// cutoff and the one-winner rule are the same as for a single approval; a ticket whose move fails is listed as skipped. With the checks as wired today (data quality is unavailable) this approves nothing and says why per ticket.
/// </summary>
internal sealed class ApproveAllHandler(
    IPlanStore store,
    IChecklistSource checklists,
    ICommandHandler<MoveTicketCommand, TicketMoveOutcome> mover,
    TimeProvider time,
    ILogger<ApproveAllHandler> logger) : ICommandHandler<ApproveAllCommand, ApproveAllOutcome>
{
    private const string Reason = "Approved with the whole plan: every check passed.";

    public async ValueTask<Result<ApproveAllOutcome>> HandleAsync(ApproveAllCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ReviewLookup.OwnerGuard(command.Actor) is { } notOwner)
        {
            return Refuse(notOwner);
        }

        var plan = await store.FindAsync(command.PlanId, cancellationToken);
        if (plan is null)
        {
            return Refuse(PlanErrors.NotFound(command.PlanId));
        }

        if (!TicketReview.IsOpen(plan, time.GetUtcNow()))
        {
            return Refuse(PlanErrors.Closed("The plan is not open for approval: it is closed or its review cutoff has passed."));
        }

        var items = await store.ItemsAsync(command.PlanId, cancellationToken);
        var approved = new List<long>();
        var skipped = new List<SkippedTicket>();
        foreach (var item in items.Where(i => i.Item.Group != TicketGroup.Rejected).OrderBy(i => i.Item.Rank))
        {
            if (item.Item.Group != TicketGroup.Primary)
            {
                skipped.Add(new SkippedTicket(item.Id, item.Item.Symbol, "reserve ticket; approve-all never takes the reserve, approve it by hand"));
                continue;
            }

            if (item.State != TicketState.Proposed)
            {
                skipped.Add(new SkippedTicket(item.Id, item.Item.Symbol, $"already {item.State.Code()}; approve-all takes only tickets nobody has decided on"));
                continue;
            }

            var results = await checklists.ForTicketAsync(item, cancellationToken);
            var reviews = TicketReview.Review(item, results, null, command.Actor);
            var why = reviews.IsFailure ? reviews.Error.Message : NotAllPassed(item, reviews.Value);
            if (why is not null)
            {
                skipped.Add(new SkippedTicket(item.Id, item.Item.Symbol, why));
                continue;
            }

            var moved = await mover.HandleAsync(new MoveTicketCommand(item.Id, TicketState.Approved, command.Actor, Reason), cancellationToken);
            if (moved.IsSuccess)
            {
                approved.Add(item.Id);
            }
            else
            {
                skipped.Add(new SkippedTicket(item.Id, item.Item.Symbol, "not approved: " + moved.Error.Message));
            }
        }

        if (approved.Count > 0)
        {
            await ReviewProgress.EnterReviewAsync(store, command.PlanId, command.Actor, cancellationToken);
        }

        ReviewLog.ApproveAll(logger, command.PlanId, approved.Count, skipped.Count);
        return Result.Success(new ApproveAllOutcome(command.PlanId, approved, skipped, PlanWording.ApprovedForWatching));
    }

    // Null when every check of every account is a pass.
    private static string? NotAllPassed(PlanItemRow item, IReadOnlyList<ChecklistReview?> reviews)
    {
        if (TicketReview.Blocked(item, reviews) is { } blocked)
        {
            return blocked;
        }

        var notPassed = reviews.SelectMany(r => r!.Checks).Where(c => c.Standing != CheckStanding.Passed).Select(c => c.Check.Code).Distinct(StringComparer.Ordinal).ToList();
        return notPassed.Count == 0 ? null : "checks not passed: " + string.Join(", ", notPassed) + "; approve it by hand";
    }

    private Result<ApproveAllOutcome> Refuse(Error error)
    {
        ReviewLog.Refused(logger, error.Code);
        return Result.Failure<ApproveAllOutcome>(error);
    }
}
