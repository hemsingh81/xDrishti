using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;

namespace XDrishti.Application.Market.Quality;

/// <summary>The most recent quality runs, newest first (take 1 to 100, default 20; out of range is clamped like the data jobs list).</summary>
public sealed record ListQualityRunsQuery(int? Take) : IQuery<IReadOnlyList<QualityRunView>>;

internal sealed class ListQualityRunsHandler(IQualityQueries queries) : IQueryHandler<ListQualityRunsQuery, IReadOnlyList<QualityRunView>>
{
    public async ValueTask<IReadOnlyList<QualityRunView>> HandleAsync(ListQualityRunsQuery query, CancellationToken cancellationToken) =>
        await queries.ListRunsAsync(Math.Clamp(query.Take ?? DataLimits.DefaultHistory, 1, DataLimits.MaxHistory), cancellationToken);
}
