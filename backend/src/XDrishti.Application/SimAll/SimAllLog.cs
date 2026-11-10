using Microsoft.Extensions.Logging;

namespace XDrishti.Application.SimAll;

/// <summary>Structured log events of the simulate-everything job (AU-12; ids 7330 to 7345). Ids, states, counts and codes only.</summary>
internal static partial class SimAllLog
{
    [LoggerMessage(EventId = 7330, Level = LogLevel.Information, Message = "simulate_all.started {JobId}: {Chunks} chunks, {Strategies} strategies")]
    public static partial void Started(ILogger logger, Guid jobId, int chunks, int strategies);

    [LoggerMessage(EventId = 7331, Level = LogLevel.Warning, Message = "simulate_all.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 7332, Level = LogLevel.Information, Message = "simulate_all.paused {JobId}: {Reason}")]
    public static partial void Paused(ILogger logger, Guid jobId, string reason);

    [LoggerMessage(EventId = 7333, Level = LogLevel.Information, Message = "simulate_all.resumed {JobId}")]
    public static partial void Resumed(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 7334, Level = LogLevel.Information, Message = "simulate_all.cancelled {JobId}")]
    public static partial void Cancelled(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 7335, Level = LogLevel.Information, Message = "simulate_all.finished {JobId}: {State}")]
    public static partial void Finished(ILogger logger, Guid jobId, string state);

    [LoggerMessage(EventId = 7336, Level = LogLevel.Information, Message = "simulate_all.redo {Chunks} chunk(s) left running by a stopped host")]
    public static partial void Redo(ILogger logger, int chunks);

    [LoggerMessage(EventId = 7337, Level = LogLevel.Information, Message = "simulate_all.chunk {JobId}: {State} in {Millis} ms ({Code})")]
    public static partial void Chunk(ILogger logger, Guid jobId, string state, long millis, string code);

    [LoggerMessage(EventId = 7338, Level = LogLevel.Error, Message = "simulate_all.chunk_crashed {JobId}")]
    public static partial void ChunkCrashed(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(EventId = 7339, Level = LogLevel.Error, Message = "simulate_all.loop_failed")]
    public static partial void LoopFailed(ILogger logger, Exception exception);
}
