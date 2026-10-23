using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>Lists the recorded attempts of the nightly pipeline (spec P7-08). Read only.</summary>
internal sealed class ListPlanRunsHandler(IPlanStore store) : IQueryHandler<ListPlanRunsQuery, Result<IReadOnlyList<PlanRunView>>>
{
    public async ValueTask<Result<IReadOnlyList<PlanRunView>>> HandleAsync(ListPlanRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 200)
        {
            return Result.Failure<IReadOnlyList<PlanRunView>>(PlanErrors.Invalid([new FieldError("limit", "The limit is 1 to 200.")]));
        }

        var rows = await store.ListRunsAsync(query.Date, query.Limit, cancellationToken);
        return Result.Success<IReadOnlyList<PlanRunView>>([.. rows.Select(PlanRunView.Of)]);
    }
}
