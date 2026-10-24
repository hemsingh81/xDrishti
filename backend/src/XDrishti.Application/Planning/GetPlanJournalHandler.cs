using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>The journal of one plan: each ticket's fills with the fill grade beside the plan-price grade, and the day's discretionary fills apart (spec P7-14). Read only.</summary>
internal sealed class GetPlanJournalHandler(IPlanStore plans, IJournalStore journal, IGradeStore grades) : IQueryHandler<GetPlanJournalQuery, Result<PlanJournalView>>
{
    public async ValueTask<Result<PlanJournalView>> HandleAsync(GetPlanJournalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = await plans.FindAsync(query.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PlanJournalView>(PlanErrors.NotFound(query.PlanId));
        }

        var items = (await plans.ItemsAsync(plan.Id, cancellationToken)).ToDictionary(i => i.Id);
        var entries = await journal.EntriesAsync(plan.Id, cancellationToken);
        var planGrades = await grades.ForPlansAsync([plan.Id], cancellationToken);
        var lines = entries.Where(e => items.ContainsKey(e.ItemId)).Select(e =>
        {
            var item = items[e.ItemId];
            var g = planGrades.TryGetValue(e.ItemId, out var s) ? s.Grade.Grade : null;
            var status = e.Grade?.Status ?? (e.Fills.Where(f => f.Role == "exit").Sum(f => f.Qty) >= e.Fills.Where(f => f.Role == "entry").Sum(f => f.Qty) ? "pending" : "open");
            return new JournalEntryView(
                e.ItemId, item.Item.Rank, item.Item.Symbol, item.Item.Side, e.AccountId, item.State.Code(),
                [.. e.Fills.Select(View)], status, e.Grade, g?.Status.Code() ?? "pending", g?.R, e.Uncertain);
        }).OrderBy(l => l.Rank).ThenBy(l => l.AccountId).ToList();
        var discretionary = (await journal.DiscretionaryAsync(plan.Plan.ForDate, cancellationToken)).Select(View).ToList();
        var withFills = lines.Select(l => (l.ItemId, l.AccountId)).ToHashSet();
        var without = items.Values
            .Where(i => i.Item.Group != TicketGroup.Rejected && (i.State is TicketState.Approved or TicketState.Armed or TicketState.Triggered or TicketState.Closed))
            .SelectMany(i => i.Item.Allocations.Select(a => (ItemId: i.Id, a.AccountId)))
            .Count(p => !withFills.Contains(p));
        return Result.Success(new PlanJournalView(plan.Id, plan.Plan.ForDate, FillGrade.Basis, TicketGrade.Basis, lines, discretionary, without));
    }

    private static JournalFillView View(StoredFill f) => new(f.TransactionId, f.Role, f.Symbol, f.Side, f.Date, f.Time, f.Qty, f.Price);
}
