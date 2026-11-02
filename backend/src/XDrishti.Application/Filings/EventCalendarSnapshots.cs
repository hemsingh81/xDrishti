using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Filings;

/// <summary>
/// <see cref="IEventCalendarSnapshots"/> (P15-06, ADR 0182): one <see cref="ICalendarRows"/> read for the instruments of a run with the cutoff <c>end of the India date of through - lag</c> (a row intimated later can be visible to no decision of the window), the lag of the
/// point-in-time store, and <see cref="EventCalendarSnapshot.Create"/>. A failing read is a failure (<c>simulation.filter_not_available</c>), not an exception: the run is refused, never run over an empty calendar. Cancellation passes through.
/// </summary>
internal sealed class EventCalendarSnapshots(ICalendarRows rows, IFilingSettings settings, ILogger<EventCalendarSnapshots> logger) : IEventCalendarSnapshots
{
    public async ValueTask<Result<EventCalendarSnapshot>> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly through, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var lag = TimeSpan.FromMinutes(settings.LagMinutes);
        var endOfThrough = new DateTimeOffset(through.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).AddTicks(-10);   // the last whole microsecond of the India date
        try
        {
            var stored = instrumentIds.Count == 0 ? [] : await rows.ResultsRowsAsync([.. instrumentIds.Distinct()], endOfThrough - lag, cancellationToken);
            return Result.Success(EventCalendarSnapshot.Create(stored, settings.LagMinutes));
        }
#pragma warning disable CA1031 // A failing calendar read refuses the run (no snapshot); cancellation is rethrown by the filter. Only the type of the failure is logged (a message can carry values).
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventFilterLog.CalendarReadFailed(logger, failure.GetType().Name);
            return Result.Failure<EventCalendarSnapshot>(Error.Validation(SimulationErrors.FilterNotAvailableCode, "The results calendar could not be read (P15-04)."));
        }
    }
}
