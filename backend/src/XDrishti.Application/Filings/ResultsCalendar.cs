using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// <see cref="IResultsCalendar"/> (P15-04): reads the stored results rows intimated at or before <c>t - lag</c> through <see cref="ICalendarRows"/> and lets <see cref="ResultsDays"/> decide. The lag is the one of the point-in-time store (<c>Filings:LagMinutes</c>); the
/// calendar is the confirmed trading calendar. No estimate, no default: an instrument without a usable row is <c>unavailable</c>.
/// </summary>
internal sealed class ResultsCalendar(ICalendarRows rows, ITradingCalendar tradingCalendar, IFilingSettings settings) : IResultsCalendar
{
    public async ValueTask<ResultsAnswer> DaysToResults(Guid instrumentId, DateTimeOffset t, CancellationToken cancellationToken)
    {
        var all = await DaysToResults([instrumentId], t, cancellationToken);
        return all[instrumentId];
    }

    public async ValueTask<IReadOnlyDictionary<Guid, ResultsAnswer>> DaysToResults(IReadOnlyCollection<Guid> instrumentIds, DateTimeOffset t, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.Distinct().ToList();
        var answers = new Dictionary<Guid, ResultsAnswer>();
        if (ids.Count == 0)
        {
            return answers;
        }

        var lag = TimeSpan.FromMinutes(settings.LagMinutes);
        var stored = await rows.ResultsRowsAsync(ids, t - lag, cancellationToken);
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var byInstrument = stored.ToLookup(r => r.Filing.InstrumentId);
        foreach (var id in ids)
        {
            answers[id] = ResultsDays.Compute(byInstrument[id], t, lag, calendar);
        }

        return answers;
    }

    public async ValueTask<IReadOnlyList<UpcomingMeeting>> UpcomingAsync(DateTimeOffset t, CancellationToken cancellationToken)
    {
        var lag = TimeSpan.FromMinutes(settings.LagMinutes);
        var stored = await rows.ResultsRowsAsync(null, t - lag, cancellationToken);
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        return [.. stored.GroupBy(r => r.Filing.InstrumentId).SelectMany(g => ResultsDays.Upcoming(g, t, lag, calendar)).OrderBy(m => m.EventSession).ThenBy(m => m.InstrumentId).ThenBy(m => m.PeriodEnd)];
    }
}
