using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.Live;

/// <summary>Structured log events of the live price sink (event ids 4081 to 4090; the flush line is Debug: one per flush would be a line every two seconds all session). Counts and instrument ids only - never a token, a client id, a URL or a raw packet; no line per tick.</summary>
internal static partial class LiveLog
{
    [LoggerMessage(EventId = 4081, Level = LogLevel.Debug, Message = "feed.flush ticks={Ticks} holdings={Holdings} quotes={Quotes} alerts_triggered={Alerts} notified={Notified} ms={Ms}")]
    public static partial void Flushed(ILogger logger, int ticks, int holdings, int quotes, int alerts, bool notified, long ms);

    [LoggerMessage(EventId = 4082, Level = LogLevel.Warning, Message = "feed.flush_failed ticks={Ticks}: the batch is kept for the next flush")]
    public static partial void FlushFailed(ILogger logger, int ticks, Exception exception);

    [LoggerMessage(EventId = 4083, Level = LogLevel.Information, Message = "feed.alert_triggered count={Count}")]
    public static partial void AlertsTriggered(ILogger logger, int count);

    [LoggerMessage(EventId = 4894, Level = LogLevel.Warning, Message = "feed.observer_failed {ExceptionType}: prices were written")]
    public static partial void ObserverFailed(ILogger logger, string exceptionType);
}
