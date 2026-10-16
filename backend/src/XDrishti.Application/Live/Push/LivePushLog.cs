using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Live.Push;

/// <summary>
/// Structured log events of the hub's push (event ids 4151 to 4165; the listener's is 4143). Counts and fixed words only - no session id, instrument id, price, connection id or exception text; nothing per push beyond a debug line.
/// </summary>
internal static partial class LivePushLog
{
    [LoggerMessage(EventId = 4151, Level = LogLevel.Information, Message = "hub.connected connections={Connections}")]
    public static partial void Connected(ILogger logger, int connections);

    [LoggerMessage(EventId = 4152, Level = LogLevel.Information, Message = "hub.disconnected connections={Connections}")]
    public static partial void Disconnected(ILogger logger, int connections);

    [LoggerMessage(EventId = 4153, Level = LogLevel.Information, Message = "hub.subscribed instruments={Instruments} truncated={Truncated}")]
    public static partial void Subscribed(ILogger logger, int instruments, bool truncated);

    [LoggerMessage(EventId = 4154, Level = LogLevel.Debug, Message = "hub.pushed connections={Connections} instruments={Instruments} messages={Messages}")]
    public static partial void Pushed(ILogger logger, int connections, int instruments, int messages);

    [LoggerMessage(EventId = 4155, Level = LogLevel.Warning, Message = "hub.push_failed type={Type}: tried again at the next notification or sweep")]
    public static partial void PushFailed(ILogger logger, string type);

    [LoggerMessage(EventId = 4156, Level = LogLevel.Warning, Message = "hub.send_failed type={Type}")]
    public static partial void SendFailed(ILogger logger, string type);

    [LoggerMessage(EventId = 4157, Level = LogLevel.Warning, Message = "hub.status_failed type={Type}")]
    public static partial void StatusFailed(ILogger logger, string type);

    [LoggerMessage(EventId = 4158, Level = LogLevel.Information, Message = "hub.session_ended connections={Connections}: disconnected")]
    public static partial void SessionEnded(ILogger logger, int connections);

    [LoggerMessage(EventId = 4159, Level = LogLevel.Warning, Message = "hub.session_check_failed type={Type}")]
    public static partial void SessionCheckFailed(ILogger logger, string type);

    [LoggerMessage(EventId = 4160, Level = LogLevel.Warning, Message = "hub.subscribe_failed type={Type}")]
    public static partial void SubscribeFailed(ILogger logger, string type);
}
