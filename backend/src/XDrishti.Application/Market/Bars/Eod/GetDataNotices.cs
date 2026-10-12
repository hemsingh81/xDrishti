using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>The data notices for the top-bar bell: a failed scheduled run, open gaps and failing data quality (see <see cref="DataNoticeRules"/>). Cheap: a few small reads.</summary>
public sealed record GetDataNoticesQuery : IQuery<DataNotices>;

internal sealed class GetDataNoticesHandler(IDataJobStore jobs, IBarGaps gaps, IQualityQueries quality, ITradingCalendar tradingCalendar, IDataSettings settings, ICorporateActionQueries corporateActions, TimeProvider clock) : IQueryHandler<GetDataNoticesQuery, DataNotices>
{
    public async ValueTask<DataNotices> HandleAsync(GetDataNoticesQuery query, CancellationToken cancellationToken)
    {
        var lastQuality = await quality.GetLastRunAsync(true, cancellationToken);
        var gate = await QualityGateReader.ReadAsync(quality, tradingCalendar, settings, clock, await quality.GetCountsAsync(cancellationToken), cancellationToken);
        return new(DataNoticeRules.Build(await jobs.GetLastByTriggerAsync(DataJobTrigger.Daily, cancellationToken), await gaps.GetSummaryAsync(cancellationToken), lastQuality, gate, await corporateActions.GetPendingAsync(cancellationToken)));
    }
}
