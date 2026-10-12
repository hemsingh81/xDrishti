using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Quality;

/// <summary>Structured log events of the data quality checks (event ids 3371 to 3389: unique across the solution, asserted by the architecture tests). Counts and symbols only.</summary>
internal static partial class QualityLog
{
    [LoggerMessage(EventId = 3371, Level = LogLevel.Information, Message = "Quality run {RunId} ({Trigger}) started: {Instruments} instruments over {Sessions} sessions")]
    public static partial void RunStarted(ILogger logger, Guid runId, string trigger, int instruments, int sessions);

    [LoggerMessage(EventId = 3372, Level = LogLevel.Information, Message = "Quality run {RunId} finished: {Passed} passed, {Warned} warnings, {Failed} failed")]
    public static partial void RunFinished(ILogger logger, Guid runId, int passed, int warned, int failed);

    [LoggerMessage(EventId = 3373, Level = LogLevel.Error, Message = "Quality run {RunId}: the checks of {Symbol} could not run")]
    public static partial void InstrumentCrashed(ILogger logger, Guid runId, string symbol, Exception exception);

    [LoggerMessage(EventId = 3374, Level = LogLevel.Error, Message = "Quality run {RunId} failed unexpectedly")]
    public static partial void RunCrashed(ILogger logger, Guid runId, Exception exception);

    [LoggerMessage(EventId = 3375, Level = LogLevel.Warning, Message = "{Count} quality run(s) that were running when the application stopped were marked failed")]
    public static partial void InterruptedRunsRecovered(ILogger logger, int count);
}
