using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Paper;

/// <summary>
/// The read-only door of the weekly review (spec P10-03, ADR 0121): it calls only the read members of the plan, grade, journal, governance and paper record. No member of this class writes, moves a ticket, approves, changes a setting or calls a broker.
/// </summary>
internal sealed class PaperReviewSource(IPlanStore plans, IGradeStore grades, IJournalStore journal, IGovernanceStore governance, IPaperReviewData data) : IPaperReviewSource
{
    private const int PlanLookup = 20;

    public ValueTask<IReadOnlyList<EpochSummary>> EpochsAsync(CancellationToken cancellationToken) => governance.ListEpochsAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<ReviewTicket>> TicketsAsync(DateOnly session, CancellationToken cancellationToken)
    {
        var plan = (await plans.ListAsync(session, null, PlanLookup, cancellationToken)).FirstOrDefault(p => p.Plan.ForDate == session);
        if (plan is null)
        {
            return [];
        }

        var items = (await plans.ItemsAsync(plan.Id, cancellationToken)).Where(i => i.Item.Group != TicketGroup.Rejected).ToList();
        var approvals = await plans.ApprovalsAsync(plan.Id, cancellationToken);
        var stored = await grades.ForPlansAsync([plan.Id], cancellationToken);
        return [.. items.Select(item => Ticket(session, item, approvals, stored.GetValueOrDefault(item.Id)?.Grade.Grade))];
    }

    public ValueTask<IReadOnlyList<ReviewTrade>> TradesAsync(long epochId, CancellationToken cancellationToken) => data.TradesAsync(epochId, cancellationToken);

    public ValueTask<IReadOnlyList<ReviewSkip>> SkipsAsync(long epochId, CancellationToken cancellationToken) => data.SkipsAsync(epochId, cancellationToken);

    public async ValueTask<IReadOnlyList<ReviewFill>> DiscretionaryAsync(DateOnly session, CancellationToken cancellationToken) =>
        [.. (await journal.DiscretionaryAsync(session, cancellationToken)).Select(f => new ReviewFill(f.AccountId, f.Symbol, string.Equals(f.Side, "buy", StringComparison.OrdinalIgnoreCase), f.Qty, f.Price, f.Charges))];

    private static ReviewTicket Ticket(DateOnly session, PlanItemRow item, IReadOnlyList<ApprovalRow> approvals, TicketGrade? grade)
    {
        var approve = approvals.Where(a => a.ItemId == item.Id && a.Decision == ApprovalDecision.Approve.Code()).MaxBy(a => a.Id);
        var modify = approvals.Where(a => a.ItemId == item.Id && a.Decision == ApprovalDecision.Modify.Code()).MaxBy(a => a.Id);
        var parsed = modify is null ? null : TicketModification.Parse(modify.Modifications);
        IReadOnlyList<QuantityChange> changes = parsed is { IsSuccess: true } ? parsed.Value : [];
        var allocations = item.Item.Allocations.OrderBy(a => a.AccountId)
            .Select(a => new ReviewAllocation(a.AccountId, a.Quantity, changes.FirstOrDefault(c => c.AccountId == a.AccountId) is { } c && c.Quantity >= 1 && c.Quantity < a.Quantity ? c.Quantity : a.Quantity))
            .ToList();
        return new ReviewTicket(item.PlanId, item.Id, session, item.Item.Group, item.Item.Symbol, item.Item.Side, item.State, approve?.Kind, approve?.ConfirmReason is not null, grade, allocations);
    }
}
