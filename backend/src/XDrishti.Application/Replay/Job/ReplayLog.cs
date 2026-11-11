using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Replay.Job;

/// <summary>Structured log events of the system replay (SR-01 part B; ids 7530 to 7539). Ids, states, counts, dates and codes only.</summary>
internal static partial class ReplayLog
{
    [LoggerMessage(EventId = 7530, Level = LogLevel.Information, Message = "replay.started {RunId}: {Sessions} sessions, trial {Trial} over this range")]
    public static partial void Started(ILogger logger, Guid runId, int sessions, int trial);

    [LoggerMessage(EventId = 7531, Level = LogLevel.Information, Message = "replay.progress {RunId}: {Done} of {Total} sessions, as of {Session}")]
    public static partial void Progress(ILogger logger, Guid runId, int done, int total, DateOnly session);

    [LoggerMessage(EventId = 7532, Level = LogLevel.Information, Message = "replay.yield {RunId}: waiting, {Reason}")]
    public static partial void Yield(ILogger logger, Guid runId, string reason);

    [LoggerMessage(EventId = 7533, Level = LogLevel.Information, Message = "replay.resume {RunId}: from the session after {Cursor}")]
    public static partial void Resume(ILogger logger, Guid runId, DateOnly? cursor);

    [LoggerMessage(EventId = 7534, Level = LogLevel.Information, Message = "replay.finished {RunId}: {Sessions} sessions")]
    public static partial void Finished(ILogger logger, Guid runId, int sessions);

    [LoggerMessage(EventId = 7535, Level = LogLevel.Warning, Message = "replay.failed {RunId}: {Reason}")]
    public static partial void Failed(ILogger logger, Guid runId, string reason);

    [LoggerMessage(EventId = 7536, Level = LogLevel.Information, Message = "replay.cancelled {RunId} after {Done} sessions")]
    public static partial void Cancelled(ILogger logger, Guid runId, int done);

    [LoggerMessage(EventId = 7537, Level = LogLevel.Error, Message = "replay.loop_failed")]
    public static partial void LoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7538, Level = LogLevel.Error, Message = "replay.session_crashed {RunId}")]
    public static partial void SessionCrashed(ILogger logger, Guid runId, Exception exception);

    [LoggerMessage(EventId = 7539, Level = LogLevel.Information, Message = "replay.recovered {Runs} run(s) left by a stopped host")]
    public static partial void Recovered(ILogger logger, int runs);
}
