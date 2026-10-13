using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Platform.Services;

/// <summary>Structured log events of the services framework (event ids 3401 to 3430: unique across the solution, asserted by the architecture tests). Service ids and run ids only; no parameters, no secrets.</summary>
internal static partial class ServicesLog
{
    [LoggerMessage(EventId = 3401, Level = LogLevel.Information, Message = "service.started service={ServiceId} run={RunId} trigger={Trigger} attempt={Attempt}")]
    public static partial void Started(ILogger logger, string serviceId, Guid runId, string trigger, int attempt);

    [LoggerMessage(EventId = 3402, Level = LogLevel.Information, Message = "service.finished service={ServiceId} run={RunId} status={Status}")]
    public static partial void Finished(ILogger logger, string serviceId, Guid runId, string status);

    [LoggerMessage(EventId = 3403, Level = LogLevel.Warning, Message = "service.failed service={ServiceId} run={RunId} attempt={Attempt}: {Message}")]
    public static partial void Failed(ILogger logger, string serviceId, Guid runId, int attempt, string message);

    [LoggerMessage(EventId = 3404, Level = LogLevel.Information, Message = "service.skipped service={ServiceId} trigger={Trigger}: {Reason}")]
    public static partial void Skipped(ILogger logger, string serviceId, string trigger, string reason);

    [LoggerMessage(EventId = 3405, Level = LogLevel.Information, Message = "service.retry service={ServiceId} attempt={Attempt} at {DueAt}")]
    public static partial void Retry(ILogger logger, string serviceId, int attempt, DateTimeOffset dueAt);

    [LoggerMessage(EventId = 3406, Level = LogLevel.Information, Message = "service.catch_up service={ServiceId} for the run due at {Due}")]
    public static partial void CatchUp(ILogger logger, string serviceId, DateTimeOffset due);

    [LoggerMessage(EventId = 3407, Level = LogLevel.Error, Message = "service.crashed service={ServiceId}")]
    public static partial void Crashed(ILogger logger, string serviceId, Exception exception);

    [LoggerMessage(EventId = 3408, Level = LogLevel.Error, Message = "The service scheduler could not read the schedules; it looks again in a minute")]
    public static partial void TickCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3409, Level = LogLevel.Information, Message = "{Count} service run(s) left running by the previous process were closed as interrupted")]
    public static partial void Recovered(ILogger logger, int count);

    [LoggerMessage(EventId = 3410, Level = LogLevel.Error, Message = "Interrupted service runs could not be cleaned up")]
    public static partial void RecoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3411, Level = LogLevel.Information, Message = "Service {ServiceId} started on the configured first-start settings (enabled={Enabled})")]
    public static partial void Seeded(ILogger logger, string serviceId, bool enabled);

    [LoggerMessage(EventId = 3412, Level = LogLevel.Error, Message = "The first-start settings of the services could not be stored")]
    public static partial void SeedFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3413, Level = LogLevel.Information, Message = "Service scheduler started with {Count} service(s); next look at {Next}")]
    public static partial void SchedulerStarted(ILogger logger, int count, DateTimeOffset next);

    [LoggerMessage(EventId = 3414, Level = LogLevel.Error, Message = "The run record of service {ServiceId} ({RunId}) could not be closed")]
    public static partial void CloseFailed(ILogger logger, string serviceId, Guid runId, Exception exception);

    [LoggerMessage(EventId = 3415, Level = LogLevel.Error, Message = "A service run after {ServiceId} could not be started")]
    public static partial void DependentFailed(ILogger logger, string serviceId, Exception exception);

    [LoggerMessage(EventId = 3416, Level = LogLevel.Information, Message = "service.retry_dropped service={ServiceId} attempt={Attempt}: {Reason}")]
    public static partial void RetryDropped(ILogger logger, string serviceId, int attempt, string reason);

    [LoggerMessage(EventId = 3417, Level = LogLevel.Information, Message = "service.retry_resumed service={ServiceId} attempt={Attempt} at {DueAt}: the sequence of retries went on after a restart")]
    public static partial void RetryResumed(ILogger logger, string serviceId, int attempt, DateTimeOffset dueAt);
}
