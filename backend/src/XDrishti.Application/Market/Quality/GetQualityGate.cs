using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Market.Quality;

/// <summary>The gate for the nightly plan, fail-closed: <c>blocked</c> when no finished run exists, the last finished run failed or is stale, or more than <c>Data:Quality:MaxFailedPercent</c> of the checked instruments fail (see <see cref="QualityGateRules"/>).</summary>
public sealed record GetQualityGateQuery : IQuery<QualityGateView>;

internal sealed class GetQualityGateHandler(IQualityQueries queries, ITradingCalendar tradingCalendar, IDataSettings settings, TimeProvider clock) : IQueryHandler<GetQualityGateQuery, QualityGateView>
{
    public async ValueTask<QualityGateView> HandleAsync(GetQualityGateQuery query, CancellationToken cancellationToken) =>
        await QualityGateReader.ReadAsync(queries, tradingCalendar, settings, clock, await queries.GetCountsAsync(cancellationToken), cancellationToken);
}
