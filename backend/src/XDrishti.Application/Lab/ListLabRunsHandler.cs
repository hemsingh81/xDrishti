using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Lists Lab runs (spec P5-02); an unknown state word is `lab.invalid_input`.</summary>
internal sealed class ListLabRunsHandler(ILabRunStore runs) : IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>>
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public async ValueTask<Result<IReadOnlyList<LabRunView>>> HandleAsync(ListLabRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        LabRunState? state = null;
        if (!string.IsNullOrEmpty(query.State))
        {
            if (!LabRunStates.TryParse(query.State, out var parsed))
            {
                return Result.Failure<IReadOnlyList<LabRunView>>(LabErrors.InvalidInput("`state` is one of queued, running, succeeded, failed, cancelled, interrupted."));
            }

            state = parsed;
        }

        if (query.Limit is < 1)
        {
            return Result.Failure<IReadOnlyList<LabRunView>>(LabErrors.InvalidInput("`limit` is 1 or more."));
        }

        var list = await runs.ListAsync(string.IsNullOrWhiteSpace(query.StrategyId) ? null : query.StrategyId, state, Math.Min(query.Limit ?? DefaultLimit, MaxLimit), cancellationToken);
        return Result.Success<IReadOnlyList<LabRunView>>([.. list.Select(r => LabRunView.Of(r))]);
    }
}
