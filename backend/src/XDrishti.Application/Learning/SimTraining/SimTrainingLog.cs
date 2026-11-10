using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning.SimTraining;

/// <summary>Structured log events of training from the simulation (AU-13; ids 7350 to 7355). Ids, counts and codes only.</summary>
internal static partial class SimTrainingLog
{
    [LoggerMessage(EventId = 7350, Level = LogLevel.Information, Message = "sim_training.done {JobId}: {State}, {Rows} rows, {Folds} folds, {What}")]
    public static partial void Done(ILogger logger, Guid jobId, string state, int rows, int folds, string what);

    [LoggerMessage(EventId = 7351, Level = LogLevel.Warning, Message = "sim_training.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 7352, Level = LogLevel.Warning, Message = "sim_training.failed {JobId}")]
    public static partial void Failed(ILogger logger, Guid jobId, Exception exception);
}
