using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the model registry (P6-07; ids 4520 to 4525). Model ids, the first 12 characters of a hash, counts, codes and milliseconds only: never a feature value or a trade.</summary>
internal static partial class ModelLog
{
    [LoggerMessage(EventId = 4520, Level = LogLevel.Information, Message = "learning.model.registered {ModelId} {Hash}: verdict {Verdict}, {Rows} rows, {Outcome}, total {TotalMs} ms")]
    public static partial void Registered(ILogger logger, long modelId, string hash, string verdict, int rows, string outcome, long totalMs);

    [LoggerMessage(EventId = 4521, Level = LogLevel.Warning, Message = "learning.model.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4522, Level = LogLevel.Error, Message = "learning.model.failed")]
    public static partial void Failed(ILogger logger);

    [LoggerMessage(EventId = 4523, Level = LogLevel.Information, Message = "learning.model.designated {ModelId} as {Role}")]
    public static partial void Designated(ILogger logger, long modelId, string role);

    [LoggerMessage(EventId = 4524, Level = LogLevel.Information, Message = "learning.model.scored {ModelId}")]
    public static partial void Scored(ILogger logger, long modelId);

    [LoggerMessage(EventId = 4525, Level = LogLevel.Warning, Message = "learning.model.file_refused {ModelId} {Code}")]
    public static partial void FileRefused(ILogger logger, long modelId, string code);
}
