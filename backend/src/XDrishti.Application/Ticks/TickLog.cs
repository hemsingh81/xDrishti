using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Ticks;

/// <summary>
/// Structured log events of the trade-tick recorder (event ids 5341 to 5370; P7-16b criterion 12): <c>ticks.connected|disconnected|subscribed|flush|dropped|disk_warning|disk_stop|token_refused|flapping</c>. Counts, ids and fixed words only - never the
/// URL, the token, the client id, a symbol or a price, and no text of an exception that could name the endpoint (only its type).
/// </summary>
internal static partial class TickLog
{
    [LoggerMessage(EventId = 5341, Level = LogLevel.Information, Message = "ticks.connected")]
    public static partial void Connected(ILogger logger);

    [LoggerMessage(EventId = 5342, Level = LogLevel.Information, Message = "ticks.disconnected reason={Reason}")]
    public static partial void Disconnected(ILogger logger, string reason);

    [LoggerMessage(EventId = 5343, Level = LogLevel.Information, Message = "ticks.subscribed instruments={Instruments}")]
    public static partial void Subscribed(ILogger logger, int instruments);

    [LoggerMessage(EventId = 5344, Level = LogLevel.Debug, Message = "ticks.flush rows={Rows}")]
    public static partial void Flush(ILogger logger, int rows);

    [LoggerMessage(EventId = 5345, Level = LogLevel.Warning, Message = "ticks.dropped rows={Rows} total={Total}")]
    public static partial void Dropped(ILogger logger, int rows, long total);

    [LoggerMessage(EventId = 5346, Level = LogLevel.Warning, Message = "ticks.disk_warning stored_mb={StoredMb} cap_mb={CapMb}")]
    public static partial void DiskWarning(ILogger logger, long storedMb, long capMb);

    [LoggerMessage(EventId = 5347, Level = LogLevel.Error, Message = "ticks.disk_stop reason={Reason}: recording stopped; nothing is deleted")]
    public static partial void DiskStop(ILogger logger, string reason);

    [LoggerMessage(EventId = 5348, Level = LogLevel.Error, Message = "ticks.token_refused: Dhan refused the Data account's token or plan for the tick socket; no reconnect attempts until the token changes")]
    public static partial void TokenRefused(ILogger logger);

    [LoggerMessage(EventId = 5349, Level = LogLevel.Error, Message = "ticks.flapping reconnects={Reconnects} within_minutes={Minutes}: attempts paused for {PauseMinutes} minutes")]
    public static partial void Flapping(ILogger logger, int reconnects, int minutes, int pauseMinutes);

    [LoggerMessage(EventId = 5350, Level = LogLevel.Warning, Message = "ticks.connect_failed: {Message}")]
    public static partial void ConnectFailed(ILogger logger, string message);

    [LoggerMessage(EventId = 5351, Level = LogLevel.Warning, Message = "ticks.connection_error type={ErrorType}")]
    public static partial void ConnectionError(ILogger logger, string errorType);

    [LoggerMessage(EventId = 5352, Level = LogLevel.Warning, Message = "ticks.flush_failed type={ErrorType}: the batch is kept for the next flush")]
    public static partial void FlushFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 5353, Level = LogLevel.Information, Message = "ticks.run_opened run={RunId}")]
    public static partial void RunOpened(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 5354, Level = LogLevel.Information, Message = "ticks.run_closed run={RunId} status={Status}")]
    public static partial void RunClosed(ILogger logger, Guid runId, string status);

    [LoggerMessage(EventId = 5355, Level = LogLevel.Warning, Message = "ticks.run_not_closed run={RunId}; it is closed as interrupted at the next start")]
    public static partial void RunNotClosed(ILogger logger, Guid runId, Exception exception);

    [LoggerMessage(EventId = 5356, Level = LogLevel.Warning, Message = "ticks.run_not_opened: the service has a running run already")]
    public static partial void RunNotOpened(ILogger logger);

    [LoggerMessage(EventId = 5357, Level = LogLevel.Error, Message = "ticks.supervisor_failed; the next look tries again")]
    public static partial void SupervisorFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5358, Level = LogLevel.Error, Message = "ticks.session_crashed; see the exception")]
    public static partial void SessionCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5359, Level = LogLevel.Information, Message = "ticks.set_ready instruments={Instruments} source={Source} unmapped={Unmapped} stored={Stored}")]
    public static partial void SetReady(ILogger logger, int instruments, string source, int unmapped, bool stored);

    [LoggerMessage(EventId = 5360, Level = LogLevel.Warning, Message = "ticks.crashed_day_closed days={Days}")]
    public static partial void CrashedDaysClosed(ILogger logger, int days);

    [LoggerMessage(EventId = 5361, Level = LogLevel.Warning, Message = "ticks.stop_timed_out run={RunId}")]
    public static partial void StopTimedOut(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 5362, Level = LogLevel.Information, Message = "ticks.switch_changed id={Id} enabled={Enabled}")]
    public static partial void SwitchChanged(ILogger logger, long id, bool enabled);

    [LoggerMessage(EventId = 5363, Level = LogLevel.Warning, Message = "ticks.switch_refused code={Code}")]
    public static partial void SwitchRefused(ILogger logger, string code);

    [LoggerMessage(EventId = 5364, Level = LogLevel.Warning, Message = "ticks.free_space_unreadable: only the size cap is in force")]
    public static partial void FreeSpaceUnreadable(ILogger logger);

    [LoggerMessage(EventId = 5365, Level = LogLevel.Warning, Message = "ticks.gate_read_failed type={ErrorType}: treated as closed, a running session is stopped")]
    public static partial void GateReadFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 5366, Level = LogLevel.Warning, Message = "ticks.audit_failed type={ErrorType}: the switch row stands, its audit event was not written")]
    public static partial void AuditFailed(ILogger logger, string errorType);
}
