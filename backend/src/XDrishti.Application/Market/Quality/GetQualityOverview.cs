using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Market.Quality;

/// <summary>What the Data quality screen shows above the table: the last run over every instrument, the counts and the mean score of the current results, the gate, the window and the limits.</summary>
public sealed record GetQualityOverviewQuery : IQuery<QualityOverview>;

internal sealed class GetQualityOverviewHandler(IQualityQueries queries, ITradingCalendar tradingCalendar, IDataSettings settings, ICorporateActionQueries corporateActions, TimeProvider clock)
    : IQueryHandler<GetQualityOverviewQuery, QualityOverview>
{
    public async ValueTask<QualityOverview> HandleAsync(GetQualityOverviewQuery query, CancellationToken cancellationToken)
    {
        var counts = await queries.GetCountsAsync(cancellationToken);
        var last = await queries.GetLastRunAsync(true, cancellationToken);
        var sessions = QualityWindow.Sessions(await tradingCalendar.GetAsync(cancellationToken), clock.GetUtcNow(), settings.QualitySessions);
        var window = new QualityWindowView(settings.QualitySessions, sessions.Count == 0 ? null : sessions[0], sessions.Count == 0 ? null : sessions[^1]);
        return new QualityOverview(last, counts, await QualityGateReader.ReadAsync(queries, tradingCalendar, settings, clock, counts, cancellationToken), window, settings.QualitySpikeMultiple, settings.QualityMaxMissingPercent, (await corporateActions.GetPendingAsync(cancellationToken)).Count);
    }
}
