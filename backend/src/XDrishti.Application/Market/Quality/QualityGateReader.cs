using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;

namespace XDrishti.Application.Market.Quality;

/// <summary>Reads what <see cref="QualityGateRules"/> judges (the latest finished full run, the calendar, the limit, the clock), so every screen and notice evaluates the same gate.</summary>
internal static class QualityGateReader
{
    public static async ValueTask<QualityGateView> ReadAsync(
        IQualityQueries queries,
        ITradingCalendar tradingCalendar,
        IDataSettings settings,
        TimeProvider clock,
        QualityCounts counts,
        CancellationToken cancellationToken) =>
        QualityGateRules.Evaluate(counts, await queries.GetLastFinishedFullRunAsync(cancellationToken), settings.QualityMaxFailedPercent, await tradingCalendar.GetAsync(cancellationToken), clock.GetUtcNow());
}
