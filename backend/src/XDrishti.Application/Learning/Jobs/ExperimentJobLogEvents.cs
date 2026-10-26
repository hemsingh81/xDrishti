using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>Structured log events of the replay job (P8-18 Part B; ids 4905 to 4919). Job ids, states, counts, milliseconds and codes only: never a knob value, a reason or a key.</summary>
internal static partial class ExperimentJobLogEvents
{
    [LoggerMessage(EventId = 4905, Level = LogLevel.Information, Message = "experiment.job.queued {JobId}: {Kind}, {Instruments} instruments")]
    public static partial void Queued(ILogger logger, Guid jobId, string kind, int instruments);

    [LoggerMessage(EventId = 4906, Level = LogLevel.Warning, Message = "experiment.job.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4907, Level = LogLevel.Information, Message = "experiment.job.started {JobId}")]
    public static partial void Started(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 4908, Level = LogLevel.Information, Message = "experiment.job.finished {JobId}: {State}, replay {ReplayMs} ms, compare {CompareMs} ms")]
    public static partial void Finished(ILogger logger, Guid jobId, string state, long replayMs, long compareMs);

    [LoggerMessage(EventId = 4909, Level = LogLevel.Warning, Message = "experiment.job.failed {JobId}: {Code}")]
    public static partial void Failed(ILogger logger, Guid jobId, string code);

    [LoggerMessage(EventId = 4910, Level = LogLevel.Information, Message = "experiment.job.cancelled {JobId} at {Stage}")]
    public static partial void Cancelled(ILogger logger, Guid jobId, string stage);

    [LoggerMessage(EventId = 4911, Level = LogLevel.Warning, Message = "experiment.job.interrupted {JobId}")]
    public static partial void Interrupted(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 4912, Level = LogLevel.Error, Message = "experiment.job.crashed {JobId} at {Stage}")]
    public static partial void Crashed(ILogger logger, Guid jobId, string stage, Exception exception);

    [LoggerMessage(EventId = 4913, Level = LogLevel.Error, Message = "experiment.job.dispatcher_failed")]
    public static partial void DispatcherFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4914, Level = LogLevel.Information, Message = "experiment.job.cancel_asked {JobId}: {Outcome}")]
    public static partial void CancelAsked(ILogger logger, Guid jobId, string outcome);
}
