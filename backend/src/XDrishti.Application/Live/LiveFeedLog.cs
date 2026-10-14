using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Live;

/// <summary>
/// Structured log events of the live feed (event ids 4101 to 4130; the sink's own are 4081 and 4082). Names follow P2-10 criterion 17: <c>feed.connected|disconnected|subscribed|unsubscribed|token_refused|flapping|packet_dropped</c>.
/// Counts, instrument counts and fixed reason words only - never a token, a client id, a URL or a query, a raw packet or an exception text from the connection; no line per tick.
/// </summary>
internal static partial class LiveFeedLog
{
    [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "feed.connected")]
    public static partial void Connected(ILogger logger);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Information, Message = "feed.disconnected reason={Reason}")]
    public static partial void Disconnected(ILogger logger, string reason);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Information, Message = "feed.subscribed instruments={Instruments} quote={Quote} ticker={Ticker} messages={Messages}")]
    public static partial void Subscribed(ILogger logger, int instruments, int quote, int ticker, int messages);

    [LoggerMessage(EventId = 4104, Level = LogLevel.Information, Message = "feed.unsubscribed instruments={Instruments}")]
    public static partial void Unsubscribed(ILogger logger, int instruments);

    [LoggerMessage(EventId = 4105, Level = LogLevel.Error, Message = "feed.token_refused: the Data account's token was refused; no reconnect attempts until it changes")]
    public static partial void TokenRefused(ILogger logger);

    [LoggerMessage(EventId = 4106, Level = LogLevel.Error, Message = "feed.flapping reconnects={Reconnects} within_minutes={Minutes}: attempts paused for {PauseMinutes} minutes")]
    public static partial void Flapping(ILogger logger, int reconnects, int minutes, int pauseMinutes);

    [LoggerMessage(EventId = 4107, Level = LogLevel.Warning, Message = "feed.packet_dropped new={New} total={Total}")]
    public static partial void PacketsDropped(ILogger logger, long @new, long total);

    [LoggerMessage(EventId = 4108, Level = LogLevel.Warning, Message = "feed.active_set_cut dropped={Dropped} max={Max}: the cap of Live:MaxWsInstruments was reached")]
    public static partial void SetCut(ILogger logger, int dropped, int max);

    [LoggerMessage(EventId = 4109, Level = LogLevel.Information, Message = "feed.index_missing index={Index}: not in the instrument master, skipped")]
    public static partial void IndexMissing(ILogger logger, string index);

    [LoggerMessage(EventId = 4110, Level = LogLevel.Warning, Message = "feed.connect_failed: {Reason}")]
    public static partial void ConnectFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 4111, Level = LogLevel.Warning, Message = "feed.active_set_failed: the set could not be read; the feed keeps the one it has")]
    public static partial void ActiveSetFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4112, Level = LogLevel.Warning, Message = "feed.flush_error: the flush failed; the batch stays for the next one")]
    public static partial void FlushError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4113, Level = LogLevel.Warning, Message = "feed.silent seconds={Seconds}: no packet in market hours, the connection counts as dead")]
    public static partial void Silent(ILogger logger, int seconds);

    [LoggerMessage(EventId = 4114, Level = LogLevel.Warning, Message = "feed.live_quotes_not_cleared: the table of live quotes could not be emptied at the start of the window")]
    public static partial void ClearFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4115, Level = LogLevel.Error, Message = "feed.connection_error type={Type}")]
    public static partial void ConnectionError(ILogger logger, string type);

    [LoggerMessage(EventId = 4116, Level = LogLevel.Information, Message = "feed.run_opened run={RunId} trigger={Trigger}")]
    public static partial void RunOpened(ILogger logger, Guid runId, string trigger);

    [LoggerMessage(EventId = 4117, Level = LogLevel.Information, Message = "feed.run_closed run={RunId} status={Status}: {Reason}")]
    public static partial void RunClosed(ILogger logger, Guid runId, string status, string reason);

    [LoggerMessage(EventId = 4118, Level = LogLevel.Information, Message = "feed.no_token: the Data account has no usable token; the feed does not connect")]
    public static partial void NoToken(ILogger logger);

    [LoggerMessage(EventId = 4119, Level = LogLevel.Error, Message = "feed.supervisor_failed: a look at the window failed; the next one is in a few seconds")]
    public static partial void SupervisorFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4120, Level = LogLevel.Error, Message = "feed.session_crashed: the connection manager stopped unexpectedly")]
    public static partial void SessionCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4121, Level = LogLevel.Warning, Message = "feed.run_not_opened: the service has a running run already")]
    public static partial void RunNotOpened(ILogger logger);

    [LoggerMessage(EventId = 4122, Level = LogLevel.Warning, Message = "feed.run_not_closed run={RunId}: the run record could not be closed")]
    public static partial void RunNotClosed(ILogger logger, Guid runId, Exception exception);

    [LoggerMessage(EventId = 4123, Level = LogLevel.Information, Message = "feed.interrupted_run_closed run={RunId}: the feed restarted while the run was open")]
    public static partial void InterruptedRunClosed(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 4124, Level = LogLevel.Warning, Message = "feed.stop_timed_out run={RunId}: the session did not end in time; the run stays open and no new session starts until it has")]
    public static partial void StopTimedOut(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 4125, Level = LogLevel.Information, Message = "feed.packet_summary dropped={Dropped} top=[{Top}]")]
    public static partial void PacketSummary(ILogger logger, long dropped, string top);
}
