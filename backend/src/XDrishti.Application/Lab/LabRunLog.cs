using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Lab;

/// <summary>
/// Structured log events of the Lab runs (spec P5-02; event ids 4439 to 4447). The run id, the key prefix, the stage and counts only: never a strategy text, a trade or a secret.
/// </summary>
internal static partial class LabRunLog
{
    [LoggerMessage(EventId = 4439, Level = LogLevel.Information, Message = "lab.run.queued run {RunId} strategy {StrategyId} version {Version} key {Key} instruments {Instruments} sampled {Sampled}")]
    public static partial void Queued(ILogger logger, Guid runId, string strategyId, int version, string key, int instruments, bool sampled);

    [LoggerMessage(EventId = 4440, Level = LogLevel.Information, Message = "lab.run.started run {RunId} key {Key}")]
    public static partial void Started(ILogger logger, Guid runId, string key);

    [LoggerMessage(EventId = 4441, Level = LogLevel.Information, Message = "lab.run.stage run {RunId} stage {Stage} problems {Problems}")]
    public static partial void Stage(ILogger logger, Guid runId, string stage, int problems);

    [LoggerMessage(EventId = 4442, Level = LogLevel.Information, Message = "lab.run.finished run {RunId} sim run {SimRunKey} trades {Trades} open at end {OpenAtEnd} in {Milliseconds} ms")]
    public static partial void Finished(ILogger logger, Guid runId, string simRunKey, int trades, int openAtEnd, long milliseconds);

    [LoggerMessage(EventId = 4443, Level = LogLevel.Warning, Message = "lab.run.failed run {RunId} stage {Stage} code {Code} problems {Problems}")]
    public static partial void Failed(ILogger logger, Guid runId, string stage, string code, int problems);

    [LoggerMessage(EventId = 4444, Level = LogLevel.Information, Message = "lab.run.cancelled run {RunId} stage {Stage}")]
    public static partial void Cancelled(ILogger logger, Guid runId, string stage);

    [LoggerMessage(EventId = 4445, Level = LogLevel.Warning, Message = "lab.run.interrupted run {RunId}")]
    public static partial void Interrupted(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 4446, Level = LogLevel.Error, Message = "lab.run.crashed run {RunId} stage {Stage}")]
    public static partial void Crashed(ILogger logger, Guid runId, string stage, Exception exception);

    [LoggerMessage(EventId = 4447, Level = LogLevel.Error, Message = "lab.run.dispatcher_failed; the next tick tries again")]
    public static partial void DispatcherFailed(ILogger logger, Exception exception);
}
