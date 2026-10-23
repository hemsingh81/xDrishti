using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>The grades of one plan with that day's curves and groups (spec P7-13). Read only.</summary>
internal sealed class GetPlanGradeHandler(IPlanStore plans, IGradeStore grades) : IQueryHandler<GetPlanGradeQuery, Result<PlanGradeView>>
{
    public async ValueTask<Result<PlanGradeView>> HandleAsync(GetPlanGradeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = await plans.FindAsync(query.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PlanGradeView>(PlanErrors.NotFound(query.PlanId));
        }

        var items = await plans.ItemsAsync(plan.Id, cancellationToken);
        var stored = await grades.ForPlansAsync([plan.Id], cancellationToken);
        var tickets = items.Select(i =>
        {
            var g = stored.TryGetValue(i.Id, out var s) ? s.Grade.Grade : null;
            var decision = i.Item.Group == TicketGroup.Rejected ? string.Empty : DecisionGroups.Of(i.State).ToString().ToLowerInvariant();
            return new TicketGradeView(i.Id, i.Item.Rank, i.Item.Group.Code(), i.Item.Symbol, i.Item.Side, i.State.Code(), decision, g?.Status.Code() ?? "pending", g?.R, g?.MaeR, g?.MfeR, g?.ExitReason, g?.CostBasis, g?.Detail);
        }).ToList();
        var report = GradeReport.Build(GradedItems.Of(plan.Plan.ForDate, plan.Id, items, stored));
        return Result.Success(new PlanGradeView(plan.Id, plan.Plan.ForDate, TicketGrade.Basis, tickets, tickets.Count(t => t.Status == "pending"), report));
    }
}

/// <summary>The decisions report over the graded plans of a date range (spec P7-13, doc 10 section 8). Read only.</summary>
internal sealed class GetDecisionsReportHandler(IGradeStore grades) : IQueryHandler<GetDecisionsReportQuery, Result<GradeReportData>>
{
    /// <summary>The most graded tickets one report reads; more is flagged as truncated in the notes.</summary>
    public const int MaxTickets = 5000;

    public async ValueTask<Result<GradeReportData>> HandleAsync(GetDecisionsReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From is { } f && query.To is { } t && f > t)
        {
            return Result.Failure<GradeReportData>(PlanErrors.Invalid([new FieldError("from", "From is after to.")]));
        }

        // The range is in the query, one read for the whole report, the newest plan version of each date only.
        var page = await grades.ReportItemsAsync(query.From, query.To, MaxTickets, cancellationToken);
        return Result.Success(GradeReport.Build(page.Items, page.Truncated));
    }
}

/// <summary>Joins tickets to their stored grades; a ticket without a grade is left out of the report.</summary>
internal static class GradedItems
{
    public static List<GradedItem> Of(DateOnly date, long planId, IReadOnlyList<PlanItemRow> items, IReadOnlyDictionary<long, StoredGrade> grades) =>
        [.. items.Where(i => grades.ContainsKey(i.Id)).Select(i => new GradedItem(planId, date, i.Item.Rank, i.Item.Group, i.State, grades[i.Id].Grade.Grade))];
}
