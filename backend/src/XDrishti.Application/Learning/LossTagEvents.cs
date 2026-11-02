using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Learning;

/// <summary>
/// <see cref="ILossTagEvents"/> (P15-06): the stored results filings and the current calendar meetings of the given instruments, turned into event instants by the pure <see cref="ResultsEvents"/>. At most <see cref="MaxInstruments"/> instruments in one read (more is answered
/// as not supplied, never cut silently). A failing read answers null; cancellation passes through.
/// </summary>
internal sealed class LossTagEvents(ICalendarRows calendarRows, IResultsFilingTimes filings, ITradingCalendar tradingCalendar, IFilingSettings settings, TimeProvider clock, ILogger<LossTagEvents> logger) : ILossTagEvents
{
    public const int MaxInstruments = 5000;

    public async ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<EventInstant>>?> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.Distinct().ToList();
        if (ids.Count == 0 || ids.Count > MaxInstruments)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var lag = TimeSpan.FromMinutes(settings.LagMinutes);
        try
        {
            var meetings = await calendarRows.ResultsRowsAsync(ids, now - lag, cancellationToken);
            var filed = await filings.ReadAsync(ids, cancellationToken);
            return ResultsEvents.Of(meetings, filed, await tradingCalendar.GetAsync(cancellationToken), lag, now);
        }
#pragma warning disable CA1031 // A failing read means the events are not supplied (no tag, counted unavailable); cancellation is rethrown by the filter. Only the type of the failure is logged.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventFilterLog.CalendarReadFailed(logger, failure.GetType().Name);
            return null;
        }
    }
}
