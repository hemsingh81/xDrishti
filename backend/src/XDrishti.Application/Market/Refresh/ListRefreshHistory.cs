using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Market.Refresh;

/// <summary>The most recent refreshes, newest first (1 to 100, default 20).</summary>
public sealed record ListRefreshHistoryQuery(int? Take) : IQuery<IReadOnlyList<RefreshRunView>>;

internal sealed class ListRefreshHistoryHandler(IRefreshRunStore runs) : IQueryHandler<ListRefreshHistoryQuery, IReadOnlyList<RefreshRunView>>
{
    public async ValueTask<IReadOnlyList<RefreshRunView>> HandleAsync(ListRefreshHistoryQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take ?? RefreshLimits.DefaultHistory, 1, RefreshLimits.MaxHistory);
        return [.. (await runs.ListAsync(take, cancellationToken)).Select(RefreshRunView.From)];
    }
}
