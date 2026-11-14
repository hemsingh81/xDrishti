using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Replay.Sim;

/// <summary>The log events of the day-by-day simulation (SR-04): start, settle, advance, continue, cancel, failure.</summary>
internal static partial class SimDaysLog
{
    [LoggerMessage(EventId = 2930, Level = LogLevel.Information, Message = "Simulation {RunId} started {From} to {To} (inside the hold-out: {InsideHoldOut})")]
    public static partial void Started(ILogger logger, Guid runId, DateOnly from, DateOnly to, bool insideHoldOut);

    [LoggerMessage(EventId = 2931, Level = LogLevel.Information, Message = "Simulation {RunId} settled {Session}")]
    public static partial void Settled(ILogger logger, Guid runId, DateOnly session);

    [LoggerMessage(EventId = 2932, Level = LogLevel.Information, Message = "Simulation {RunId} advanced past {Session} (complete: {Complete})")]
    public static partial void Advanced(ILogger logger, Guid runId, DateOnly session, bool complete);

    [LoggerMessage(EventId = 2933, Level = LogLevel.Information, Message = "Simulation {RunId} continued to {End}")]
    public static partial void Continued(ILogger logger, Guid runId, DateOnly end);

    [LoggerMessage(EventId = 2934, Level = LogLevel.Information, Message = "Simulation {RunId} cancelled")]
    public static partial void Cancelled(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 2935, Level = LogLevel.Warning, Message = "Simulation {RunId} failed: {Code}")]
    public static partial void Failed(ILogger logger, Guid runId, string code);

    [LoggerMessage(EventId = 2936, Level = LogLevel.Error, Message = "The simulation loop failed a step")]
    public static partial void LoopFailed(ILogger logger, Exception exception);
}
