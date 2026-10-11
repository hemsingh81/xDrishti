using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Structured log events of the holdings module. Only account slugs, counts and outcome codes are logged — never tokens, URLs, Dhan's raw
/// answers or holding details.
/// </summary>
internal static partial class PortfolioLog
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Holdings sync {RunId} of account {Slug} started ({Trigger})")]
    public static partial void SyncStarted(ILogger logger, Guid runId, string slug, string trigger);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information, Message = "Holdings sync {RunId} done: {Added} new, {Increased} increased, {Decreased} decreased, {Closed} closed, {Unchanged} unchanged")]
    public static partial void SyncSucceeded(ILogger logger, Guid runId, int added, int increased, int decreased, int closed, int unchanged);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "Holdings sync {RunId} failed: {Reason}")]
    public static partial void SyncFailed(ILogger logger, Guid runId, string reason);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Error, Message = "Holdings sync {RunId} failed unexpectedly")]
    public static partial void SyncCrashed(ILogger logger, Guid runId, Exception exception);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Information, Message = "Automatic holdings sync of account {Slug} skipped: not connected to Dhan")]
    public static partial void AutoSyncSkipped(ILogger logger, string slug);

    [LoggerMessage(EventId = 4006, Level = LogLevel.Information, Message = "Automatic holdings refresh is off (Portfolio:AutoRefresh:Enabled = false)")]
    public static partial void AutoRefreshOff(ILogger logger);

    [LoggerMessage(EventId = 4007, Level = LogLevel.Error, Message = "The automatic holdings refresh of account {Slug} could not be run")]
    public static partial void AutoRefreshCrashed(ILogger logger, string slug, Exception exception);

    [LoggerMessage(EventId = 4008, Level = LogLevel.Error, Message = "The automatic holdings refresh could not evaluate the accounts")]
    public static partial void AutoRefreshTickCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4013, Level = LogLevel.Warning, Message = "Holdings sync {RunId} ({Trigger}) held back: Dhan's answer would close all or most holdings; confirmation needed")]
    public static partial void SyncHeldBack(ILogger logger, Guid runId, string trigger);

    [LoggerMessage(EventId = 4014, Level = LogLevel.Warning, Message = "Old holdings sync records could not be pruned")]
    public static partial void PruneFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4012, Level = LogLevel.Error, Message = "Holdings syncs that an earlier process left running could not be cleaned up")]
    public static partial void RecoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4009, Level = LogLevel.Warning, Message = "{Count} holdings sync(s) that were running when the application stopped were marked failed")]
    public static partial void InterruptedRunsRecovered(ILogger logger, int count);

    [LoggerMessage(EventId = 4010, Level = LogLevel.Information, Message = "Holdings of account {Slug} imported: {Imported} imported, {AlreadyImported} already imported, {Unknown} unknown")]
    public static partial void Imported(ILogger logger, string slug, int imported, int alreadyImported, int unknown);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Information, Message = "A lot date of account {Slug} was changed")]
    public static partial void LotDateChanged(ILogger logger, string slug);
}
