using Microsoft.Extensions.Logging;

namespace XDrishti.Application.OpeningBars;

/// <summary>Structured log events of the opening-minute recorder (event ids 3560 to 3569, BK-03b criterion 13): <c>feed.opening_bars</c> with counts and milliseconds only - never a symbol, a token, a URL or the text of an exception (only its type).</summary>
internal static partial class OpeningBarsLog
{
    [LoggerMessage(EventId = 3560, Level = LogLevel.Information, Message = "feed.opening_bars run ended: {End}, {Instruments} instruments, {Bars} bars, {Missing} missing minutes, {Late} late, {Duplicates} duplicates, {Gaps} gaps, {Milliseconds} ms")]
    public static partial void RunEnded(ILogger logger, string end, int instruments, long bars, long missing, long late, long duplicates, int gaps, long milliseconds);

    [LoggerMessage(EventId = 3561, Level = LogLevel.Information, Message = "feed.opening_bars subscribed {Instruments} instruments")]
    public static partial void Subscribed(ILogger logger, int instruments);

    [LoggerMessage(EventId = 3562, Level = LogLevel.Warning, Message = "feed.opening_bars connection problem {Kind}")]
    public static partial void Problem(ILogger logger, string kind);

    [LoggerMessage(EventId = 3563, Level = LogLevel.Warning, Message = "feed.opening_bars flush failed {Kind}; the bars stay queued")]
    public static partial void FlushFailed(ILogger logger, string kind);

    [LoggerMessage(EventId = 3564, Level = LogLevel.Error, Message = "feed.opening_bars supervisor look failed")]
    public static partial void SupervisorFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3565, Level = LogLevel.Information, Message = "feed.opening_bars freeze: {Waiting} waiting, {Formed} written")]
    public static partial void Frozen(ILogger logger, int waiting, int formed);

    [LoggerMessage(EventId = 3567, Level = LogLevel.Warning, Message = "feed.opening_bars switch audit event not written {Kind}")]
    public static partial void AuditFailed(ILogger logger, string kind);

    [LoggerMessage(EventId = 3568, Level = LogLevel.Information, Message = "feed.opening_bars retention removed {Rows} rows")]
    public static partial void Pruned(ILogger logger, int rows);

    [LoggerMessage(EventId = 3566, Level = LogLevel.Warning, Message = "feed.opening_bars stopped: low free disk space")]
    public static partial void LowDisk(ILogger logger);
}
