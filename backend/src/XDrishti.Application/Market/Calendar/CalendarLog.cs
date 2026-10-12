using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Calendar;

/// <summary>Structured log events of the trading calendar (event ids 3351 to 3369: unique across the solution, asserted by the architecture tests). Counts and dates only.</summary>
internal static partial class CalendarLog
{
    [LoggerMessage(EventId = 3351, Level = LogLevel.Information, Message = "Trading calendar from NSE's list: {Added} added, {Updated} updated, {Unchanged} unchanged, {Skipped} left out")]
    public static partial void NseApplied(ILogger logger, int added, int updated, int unchanged, int skipped);

    [LoggerMessage(EventId = 3352, Level = LogLevel.Warning, Message = "Trading calendar: NSE's holiday list was not used: {Reason}")]
    public static partial void NseUnavailable(ILogger logger, string reason);

    [LoggerMessage(EventId = 3353, Level = LogLevel.Information, Message = "Trading calendar from the stored bars: {Added} proposed, {Updated} changed, {Removed} withdrawn")]
    public static partial void ObservedApplied(ILogger logger, int added, int updated, int removed);

    [LoggerMessage(EventId = 3354, Level = LogLevel.Information, Message = "Trading calendar: the stored bars were not looked at, some instruments have bars that are not aggregated yet")]
    public static partial void ObservedSkipped(ILogger logger);

    [LoggerMessage(EventId = 3355, Level = LogLevel.Error, Message = "Trading calendar upkeep after a data job failed")]
    public static partial void UpkeepCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3356, Level = LogLevel.Warning, Message = "Trading calendar: the changes collided with another change and were dropped")]
    public static partial void SaveCollided(ILogger logger);
}
