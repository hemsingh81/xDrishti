using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Depth;

/// <summary>
/// Structured log events of the depth recorder (event ids 5301 to 5330; P7-16 criterion 11): <c>depth.connected|disconnected|subscribed|flush|dropped|disk_warning|disk_stop|token_refused|flapping</c>. Counts and fixed words only - never the URL, the token, the
/// client id, a symbol or a price, and no text of an exception that could name the endpoint (only its type).
/// </summary>
internal static partial class DepthLog
{
    [LoggerMessage(EventId = 5301, Level = LogLevel.Information, Message = "depth.connected")]
    public static partial void Connected(ILogger logger);

    [LoggerMessage(EventId = 5302, Level = LogLevel.Information, Message = "depth.disconnected reason={Reason}")]
    public static partial void Disconnected(ILogger logger, string reason);

    [LoggerMessage(EventId = 5303, Level = LogLevel.Information, Message = "depth.subscribed instruments={Instruments}")]
    public static partial void Subscribed(ILogger logger, int instruments);

    [LoggerMessage(EventId = 5304, Level = LogLevel.Debug, Message = "depth.flush rows={Rows}")]
    public static partial void Flush(ILogger logger, int rows);

    [LoggerMessage(EventId = 5305, Level = LogLevel.Warning, Message = "depth.dropped rows={Rows} total={Total}")]
    public static partial void Dropped(ILogger logger, int rows, long total);

    [LoggerMessage(EventId = 5306, Level = LogLevel.Warning, Message = "depth.disk_warning stored_mb={StoredMb} cap_mb={CapMb}")]
    public static partial void DiskWarning(ILogger logger, long storedMb, long capMb);

    [LoggerMessage(EventId = 5307, Level = LogLevel.Error, Message = "depth.disk_stop reason={Reason}: recording stopped; nothing is deleted")]
    public static partial void DiskStop(ILogger logger, string reason);

    [LoggerMessage(EventId = 5308, Level = LogLevel.Error, Message = "depth.token_refused: Dhan refused the Data account's token or plan for depth; no reconnect attempts until the token changes")]
    public static partial void TokenRefused(ILogger logger);

    [LoggerMessage(EventId = 5309, Level = LogLevel.Error, Message = "depth.flapping reconnects={Reconnects} within_minutes={Minutes}: attempts paused for {PauseMinutes} minutes")]
    public static partial void Flapping(ILogger logger, int reconnects, int minutes, int pauseMinutes);

    [LoggerMessage(EventId = 5310, Level = LogLevel.Warning, Message = "depth.connect_failed: {Message}")]
    public static partial void ConnectFailed(ILogger logger, string message);

    [LoggerMessage(EventId = 5311, Level = LogLevel.Warning, Message = "depth.connection_error type={ErrorType}")]
    public static partial void ConnectionError(ILogger logger, string errorType);

    [LoggerMessage(EventId = 5312, Level = LogLevel.Warning, Message = "depth.flush_failed type={ErrorType}: the batch is kept for the next flush")]
    public static partial void FlushFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 5313, Level = LogLevel.Information, Message = "depth.run_opened run={RunId}")]
    public static partial void RunOpened(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 5314, Level = LogLevel.Information, Message = "depth.run_closed run={RunId} status={Status}")]
    public static partial void RunClosed(ILogger logger, Guid runId, string status);

    [LoggerMessage(EventId = 5315, Level = LogLevel.Warning, Message = "depth.run_not_closed run={RunId}; it is closed as interrupted at the next start")]
    public static partial void RunNotClosed(ILogger logger, Guid runId, Exception exception);

    [LoggerMessage(EventId = 5316, Level = LogLevel.Warning, Message = "depth.run_not_opened: the service has a running run already")]
    public static partial void RunNotOpened(ILogger logger);

    [LoggerMessage(EventId = 5317, Level = LogLevel.Error, Message = "depth.supervisor_failed; the next look tries again")]
    public static partial void SupervisorFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5318, Level = LogLevel.Error, Message = "depth.session_crashed; see the exception")]
    public static partial void SessionCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5319, Level = LogLevel.Information, Message = "depth.set_ready instruments={Instruments} unmapped={Unmapped} stored={Stored}")]
    public static partial void SetReady(ILogger logger, int instruments, int unmapped, bool stored);

    [LoggerMessage(EventId = 5320, Level = LogLevel.Warning, Message = "depth.crashed_day_closed days={Days}")]
    public static partial void CrashedDaysClosed(ILogger logger, int days);

    [LoggerMessage(EventId = 5321, Level = LogLevel.Warning, Message = "depth.stop_timed_out run={RunId}")]
    public static partial void StopTimedOut(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 5322, Level = LogLevel.Information, Message = "depth.switch_changed id={Id} enabled={Enabled}")]
    public static partial void SwitchChanged(ILogger logger, long id, bool enabled);

    [LoggerMessage(EventId = 5323, Level = LogLevel.Warning, Message = "depth.switch_refused code={Code}")]
    public static partial void SwitchRefused(ILogger logger, string code);

    [LoggerMessage(EventId = 5325, Level = LogLevel.Warning, Message = "depth.gate_read_failed type={ErrorType}: treated as closed, a running session is stopped")]
    public static partial void GateReadFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 5326, Level = LogLevel.Warning, Message = "depth.audit_failed type={ErrorType}: the switch row stands, its audit event was not written")]
    public static partial void AuditFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 5324, Level = LogLevel.Warning, Message = "depth.free_space_unreadable: only the size cap is in force")]
    public static partial void FreeSpaceUnreadable(ILogger logger);
}
