using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>Lists the plans (spec P7-03). Read only; an empty list is an answer.</summary>
internal sealed class ListPlansHandler(IPlanStore store) : IQueryHandler<ListPlansQuery, Result<IReadOnlyList<PlanView>>>
{
    public async ValueTask<Result<IReadOnlyList<PlanView>>> HandleAsync(ListPlansQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 200)
        {
            return Result.Failure<IReadOnlyList<PlanView>>(PlanErrors.Invalid([new FieldError("limit", "The limit is 1 to 200.")]));
        }

        var rows = await store.ListAsync(query.Date, query.AccountId, query.Limit, cancellationToken);
        return Result.Success<IReadOnlyList<PlanView>>([.. rows.Select(r => PlanView.Of(r, [], []))]);
    }
}
