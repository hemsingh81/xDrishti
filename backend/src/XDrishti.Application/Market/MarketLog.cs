using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market;

/// <summary>Structured log events of the instrument master. Counts, phases and outcomes only: never URLs, file content or secrets (none are involved).</summary>
internal static partial class MarketLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Instrument refresh {RunId} started ({Trigger}, force: {Force})")]
    public static partial void RefreshStarted(ILogger logger, Guid runId, XDrishti.Domain.Market.RefreshTrigger trigger, bool force);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Instrument refresh {RunId} unchanged: the source file has not changed")]
    public static partial void RefreshUnchanged(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "Instrument refresh {RunId} done: {Added} added, {Updated} updated, {Retired} retired, {Revived} revived, {Unchanged} unchanged, {Total} active")]
    public static partial void RefreshSucceeded(ILogger logger, Guid runId, int added, int updated, int retired, int revived, int unchanged, int total);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "Instrument refresh {RunId} failed: {Reason}")]
    public static partial void RefreshFailed(ILogger logger, Guid runId, string reason);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Error, Message = "Instrument refresh {RunId} failed unexpectedly")]
    public static partial void RefreshCrashed(ILogger logger, Guid runId, Exception exception);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning, Message = "Instrument refresh {RunId}: the sector list is not used: {Reason}")]
    public static partial void SectorsSkipped(ILogger logger, Guid runId, string reason);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information, Message = "Daily instrument refresh is off (Instruments:AutoRefresh:Enabled = false)")]
    public static partial void AutoRefreshOff(ILogger logger);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Information, Message = "Next daily instrument refresh at {At:yyyy-MM-dd HH:mm zzz}")]
    public static partial void AutoRefreshScheduled(ILogger logger, DateTimeOffset at);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Information, Message = "Daily instrument refresh skipped: another refresh is running")]
    public static partial void AutoRefreshSkipped(ILogger logger);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Error, Message = "Daily instrument refresh could not be started")]
    public static partial void AutoRefreshCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Warning, Message = "{Count} instrument refresh(es) that were running when the application stopped were marked failed")]
    public static partial void InterruptedRunsRecovered(ILogger logger, int count);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Information, Message = "The instrument master has not been refreshed since the last scheduled time; refreshing now")]
    public static partial void AutoRefreshCatchUp(ILogger logger);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Warning, Message = "Instrument refresh {RunId}: refusing to retire most of the list: {New} instruments in the file, {Active} active")]
    public static partial void RefreshTripwire(ILogger logger, Guid runId, int @new, int active);
}
