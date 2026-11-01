using Microsoft.Extensions.Logging;

namespace XDrishti.Application.OptionChain;

/// <summary>
/// Structured log events of the option-chain recorder (event ids 6911 to 6940; P14-02 criterion 11): <c>options.chain.request|gap|slot|disk_warning|disk_stop|token_refused|paused</c> and the supervisor's. Counts, ids and fixed outcome words only - never the
/// URL, the token, the client id, the body, a symbol or a price, and no text of an exception that could name the endpoint (only its type).
/// </summary>
internal static partial class OptionChainLog
{
    [LoggerMessage(EventId = 6911, Level = LogLevel.Debug, Message = "options.chain.request kind={Kind} outcome={Outcome} status={Status} rows={Rows} latency_ms={LatencyMs}")]
    public static partial void Request(ILogger logger, string kind, string outcome, int status, int rows, int latencyMs);

    [LoggerMessage(EventId = 6912, Level = LogLevel.Warning, Message = "options.chain.gap kind={Kind} outcome={Outcome} code={Code}")]
    public static partial void Gap(ILogger logger, string kind, string outcome, string code);

    [LoggerMessage(EventId = 6913, Level = LogLevel.Information, Message = "options.chain.slot index={Index} requests={Requests} ok={Ok} gaps={Gaps} rows={Rows}")]
    public static partial void Slot(ILogger logger, int index, int requests, int ok, int gaps, long rows);

    [LoggerMessage(EventId = 6914, Level = LogLevel.Warning, Message = "options.chain.disk_warning stored_mb={StoredMb} cap_mb={CapMb}")]
    public static partial void DiskWarning(ILogger logger, long storedMb, long capMb);

    [LoggerMessage(EventId = 6915, Level = LogLevel.Error, Message = "options.chain.disk_stop reason={Reason}: recording stopped; nothing is deleted")]
    public static partial void DiskStop(ILogger logger, string reason);

    [LoggerMessage(EventId = 6916, Level = LogLevel.Error, Message = "options.chain.token_refused: Dhan refused the Data account's token or plan; nothing is sent until the token changes")]
    public static partial void TokenRefused(ILogger logger);

    [LoggerMessage(EventId = 6917, Level = LogLevel.Error, Message = "options.chain.paused slots_in_a_row={Slots} pause_minutes={Minutes}: Dhan's rate limit was hit in each of them")]
    public static partial void Paused(ILogger logger, int slots, int minutes);

    [LoggerMessage(EventId = 6918, Level = LogLevel.Warning, Message = "options.chain.store_failed type={ErrorType}: the request's rows were not stored")]
    public static partial void StoreFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 6919, Level = LogLevel.Information, Message = "options.chain.run_opened run={RunId}")]
    public static partial void RunOpened(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 6920, Level = LogLevel.Information, Message = "options.chain.run_closed run={RunId} status={Status}")]
    public static partial void RunClosed(ILogger logger, Guid runId, string status);

    [LoggerMessage(EventId = 6921, Level = LogLevel.Warning, Message = "options.chain.run_not_closed run={RunId}; it is closed as interrupted at the next start")]
    public static partial void RunNotClosed(ILogger logger, Guid runId, Exception exception);

    [LoggerMessage(EventId = 6922, Level = LogLevel.Warning, Message = "options.chain.run_not_opened: the service has a running run already")]
    public static partial void RunNotOpened(ILogger logger);

    [LoggerMessage(EventId = 6923, Level = LogLevel.Error, Message = "options.chain.supervisor_failed; the next look tries again")]
    public static partial void SupervisorFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6924, Level = LogLevel.Error, Message = "options.chain.session_crashed; see the exception")]
    public static partial void SessionCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6925, Level = LogLevel.Information, Message = "options.chain.targets indices={Indices} stocks={Stocks}")]
    public static partial void Targets(ILogger logger, int indices, int stocks);

    [LoggerMessage(EventId = 6926, Level = LogLevel.Warning, Message = "options.chain.crashed_day_closed days={Days}")]
    public static partial void CrashedDaysClosed(ILogger logger, int days);

    [LoggerMessage(EventId = 6927, Level = LogLevel.Warning, Message = "options.chain.stop_timed_out run={RunId}")]
    public static partial void StopTimedOut(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 6928, Level = LogLevel.Information, Message = "options.chain.switch_changed id={Id} enabled={Enabled}")]
    public static partial void SwitchChanged(ILogger logger, long id, bool enabled);

    [LoggerMessage(EventId = 6929, Level = LogLevel.Warning, Message = "options.chain.switch_refused code={Code}")]
    public static partial void SwitchRefused(ILogger logger, string code);

    [LoggerMessage(EventId = 6930, Level = LogLevel.Warning, Message = "options.chain.gate_read_failed type={ErrorType}: treated as closed, a running session is stopped")]
    public static partial void GateReadFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 6931, Level = LogLevel.Warning, Message = "options.chain.audit_failed type={ErrorType}: the switch row stands, its audit event was not written")]
    public static partial void AuditFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 6932, Level = LogLevel.Warning, Message = "options.chain.free_space_unreadable: only the size cap is in force")]
    public static partial void FreeSpaceUnreadable(ILogger logger);
}
