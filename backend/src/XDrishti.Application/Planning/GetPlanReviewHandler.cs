using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>The review of a plan (spec P7-10): every primary and reserve ticket with its stored checklist per account, what still needs the owner's confirmation, and whether approve-all would take it. Read only; it changes nothing.</summary>
internal sealed class GetPlanReviewHandler(IPlanStore store, IChecklistSource checklists, TimeProvider time) : IQueryHandler<GetPlanReviewQuery, Result<PlanReviewView>>
{
    public async ValueTask<Result<PlanReviewView>> HandleAsync(GetPlanReviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = await store.FindAsync(query.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PlanReviewView>(PlanErrors.NotFound(query.PlanId));
        }

        var items = await store.ItemsAsync(query.PlanId, cancellationToken);
        var approvals = await store.ApprovalsAsync(query.PlanId, cancellationToken);
        var tickets = new List<ReviewTicketView>();
        foreach (var item in items.Where(i => i.Item.Group != TicketGroup.Rejected).OrderBy(i => i.Item.Rank))
        {
            var results = await checklists.ForTicketAsync(item, cancellationToken);
            var reviews = TicketReview.Review(item, results, null, PlanActors.OwnerPrefix + "review").Value;
            var modification = TicketReview.Modification(item, approvals);
            var changes = modification.IsSuccess ? modification.Value : null;
            var accounts = new List<ReviewAccountView>();
            for (var i = 0; i < item.Item.Allocations.Count; i++)
            {
                var allocation = item.Item.Allocations[i];
                var review = reviews[i];
                var effective = changes?.FirstOrDefault(c => c.AccountId == allocation.AccountId)?.Quantity ?? allocation.Quantity;
                var checks = review is null ? [] : review.Checks.Select(c => new ReviewCheckView(c.Check.Code, c.Check.Outcome.ToString().ToLowerInvariant(), c.Standing.ToString().ToLowerInvariant(), c.Check.Detail)).ToList();
                accounts.Add(new ReviewAccountView(allocation.AccountId, allocation.Mode, allocation.Quantity, effective, review?.CanApprove ?? false, checks));
            }

            var recorded = reviews.Count > 0 && reviews.All(r => r is not null);
            var canApprove = TicketReview.Blocked(item, reviews) is null;
            var allPassed = canApprove && reviews.All(r => r!.Checks.All(c => c.Standing == CheckStanding.Passed));
            var confirmed = approvals.Where(a => a.ItemId == item.Id && a.Decision == "approve" && a.ConfirmReason is not null).OrderByDescending(a => a.Id).FirstOrDefault()?.ConfirmReason;
            var latestModify = approvals.Where(a => a.ItemId == item.Id && a.Decision == "modify").OrderByDescending(a => a.Id).FirstOrDefault()?.Modifications;
            tickets.Add(new ReviewTicketView(
                item.Id, item.Item.Rank, item.Item.Group.Code(), item.State.Code(), item.Item.Symbol, item.Item.Side, recorded, canApprove,
                TicketReview.NeedsConfirmation(item, reviews), allPassed && item.Item.Group == TicketGroup.Primary && item.State == TicketState.Proposed, latestModify, confirmed, accounts));
        }

        return Result.Success(new PlanReviewView(plan.Id, plan.Plan.ForDate, plan.State.Code(), plan.Plan.ReviewCutoff, TicketReview.IsOpen(plan, time.GetUtcNow()), PlanWording.ApprovedForWatching, tickets));
    }
}
