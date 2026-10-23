using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>One plan with its tickets and trail (spec P7-03). Read only.</summary>
internal sealed class GetPlanHandler(IPlanStore store) : IQueryHandler<GetPlanQuery, Result<PlanView>>
{
    public async ValueTask<Result<PlanView>> HandleAsync(GetPlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var row = await store.FindAsync(query.Id, cancellationToken);
        if (row is null)
        {
            return Result.Failure<PlanView>(PlanErrors.NotFound(query.Id));
        }

        var items = await store.ItemsAsync(query.Id, cancellationToken);
        var history = await store.HistoryAsync(query.Id, cancellationToken);
        return Result.Success(PlanView.Of(
            row,
            [.. items.Select(PlanItemView.Of)],
            [.. history.Select(h => new PlanEventView(h.Id, h.ItemId, h.From, h.To, h.Actor, h.Reason, h.ApprovalId, h.At))]));
    }
}
