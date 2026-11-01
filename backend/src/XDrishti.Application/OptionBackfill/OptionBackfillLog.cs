using Microsoft.Extensions.Logging;

namespace XDrishti.Application.OptionBackfill;

/// <summary>
/// Structured log events of the expired-options backfill (event ids 6941 to 6970; P14-03): <c>options.backfill.window|gap|given_up|disk_stop|token_refused|done</c> and the supervisor's. Counts, ids and fixed outcome words only - never the URL, the
/// token, the client id, the body, a symbol or a price, and no text of an exception that could name the endpoint (only its type).
/// </summary>
internal static partial class OptionBackfillLog
{
    [LoggerMessage(EventId = 6941, Level = LogLevel.Debug, Message = "options.backfill.window outcome={Outcome} status={Status} bars={Bars} latency_ms={LatencyMs}")]
    public static partial void Window(ILogger logger, string outcome, int status, int bars, int latencyMs);

    [LoggerMessage(EventId = 6942, Level = LogLevel.Warning, Message = "options.backfill.gap outcome={Outcome} code={Code} attempt={Attempt} underlying={Underlying} series={Series} from={From}")]
    public static partial void Gap(ILogger logger, string outcome, string code, int attempt, Guid underlying, string series, DateOnly from);

    [LoggerMessage(EventId = 6943, Level = LogLevel.Warning, Message = "options.backfill.given_up attempts={Attempts} underlying={Underlying} series={Series} from={From}: the key is left and listed; it is not fetched again")]
    public static partial void GivenUp(ILogger logger, int attempts, Guid underlying, string series, DateOnly from);

    [LoggerMessage(EventId = 6944, Level = LogLevel.Warning, Message = "options.backfill.disk_warning stored_mb={StoredMb} cap_mb={CapMb}")]
    public static partial void DiskWarning(ILogger logger, long storedMb, long capMb);

    [LoggerMessage(EventId = 6945, Level = LogLevel.Error, Message = "options.backfill.disk_stop reason={Reason}: the backfill stopped; nothing is deleted")]
    public static partial void DiskStop(ILogger logger, string reason);

    [LoggerMessage(EventId = 6946, Level = LogLevel.Error, Message = "options.backfill.token_refused: Dhan refused the Data account's token or plan; nothing is sent until the token changes")]
    public static partial void TokenRefused(ILogger logger);

    [LoggerMessage(EventId = 6947, Level = LogLevel.Information, Message = "options.backfill.done requests={Requests} ok={Ok} empty={Empty} gaps={Gaps} given_up={GivenUp} bars={Bars}")]
    public static partial void Done(ILogger logger, int requests, int ok, int empty, int gaps, int givenUp, long bars);

    [LoggerMessage(EventId = 6948, Level = LogLevel.Warning, Message = "options.backfill.rate_limited pause_seconds={Seconds} spacing_ms={SpacingMs}: the rate is halved for the rest of the night")]
    public static partial void RateLimited(ILogger logger, int seconds, int spacingMs);

    [LoggerMessage(EventId = 6949, Level = LogLevel.Warning, Message = "options.backfill.store_failed type={ErrorType}: the attempt's bars were not stored")]
    public static partial void StoreFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 6950, Level = LogLevel.Information, Message = "options.backfill.run_opened run={RunId} planned={Planned}")]
    public static partial void RunOpened(ILogger logger, Guid runId, int planned);

    [LoggerMessage(EventId = 6951, Level = LogLevel.Information, Message = "options.backfill.run_closed run={RunId} status={Status}")]
    public static partial void RunClosed(ILogger logger, Guid runId, string status);

    [LoggerMessage(EventId = 6952, Level = LogLevel.Warning, Message = "options.backfill.run_not_closed run={RunId}; it is closed as interrupted at the next start")]
    public static partial void RunNotClosed(ILogger logger, Guid runId, Exception exception);

    [LoggerMessage(EventId = 6953, Level = LogLevel.Warning, Message = "options.backfill.run_not_opened: the service has a running run already")]
    public static partial void RunNotOpened(ILogger logger);

    [LoggerMessage(EventId = 6954, Level = LogLevel.Error, Message = "options.backfill.supervisor_failed; the next look tries again")]
    public static partial void SupervisorFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6955, Level = LogLevel.Error, Message = "options.backfill.session_crashed; see the exception")]
    public static partial void SessionCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6956, Level = LogLevel.Information, Message = "options.backfill.targets indices={Indices} stocks={Stocks}")]
    public static partial void Targets(ILogger logger, int indices, int stocks);

    [LoggerMessage(EventId = 6957, Level = LogLevel.Warning, Message = "options.backfill.stop_timed_out run={RunId}")]
    public static partial void StopTimedOut(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 6958, Level = LogLevel.Information, Message = "options.backfill.switch_changed id={Id} enabled={Enabled}")]
    public static partial void SwitchChanged(ILogger logger, long id, bool enabled);

    [LoggerMessage(EventId = 6959, Level = LogLevel.Warning, Message = "options.backfill.switch_refused code={Code}")]
    public static partial void SwitchRefused(ILogger logger, string code);

    [LoggerMessage(EventId = 6960, Level = LogLevel.Warning, Message = "options.backfill.gate_read_failed type={ErrorType}: treated as closed, a running session is stopped")]
    public static partial void GateReadFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 6961, Level = LogLevel.Warning, Message = "options.backfill.audit_failed type={ErrorType}: the row stands, its audit event was not written")]
    public static partial void AuditFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 6962, Level = LogLevel.Warning, Message = "options.backfill.free_space_unreadable: only the size cap is in force")]
    public static partial void FreeSpaceUnreadable(ILogger logger);

    [LoggerMessage(EventId = 6963, Level = LogLevel.Information, Message = "options.backfill.probe_accepted id={Id}")]
    public static partial void ProbeAccepted(ILogger logger, long id);

    [LoggerMessage(EventId = 6964, Level = LogLevel.Warning, Message = "options.backfill.progress_failed type={ErrorType}: the progress figures are not refreshed")]
    public static partial void ProgressFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 6965, Level = LogLevel.Information, Message = "options.backfill.daily_cap requests={Requests}: the run goes on the next India day")]
    public static partial void DailyCap(ILogger logger, int requests);

    [LoggerMessage(EventId = 6966, Level = LogLevel.Error, Message = "options.backfill.run_breaker reason={Reason}: the run ends instead of going on")]
    public static partial void RunBreaker(ILogger logger, string reason);
}
