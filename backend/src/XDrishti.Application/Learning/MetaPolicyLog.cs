using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the meta-labelling policy (P6-13; ids 4570 to 4572). Counts, codes and milliseconds only: never a trade.</summary>
internal static partial class MetaPolicyLog
{
    [LoggerMessage(EventId = 4570, Level = LogLevel.Information, Message = "learning.policy.completed {Outcome}, {Rows} rows, {Folds} folds, model {ModelId}, total {TotalMs} ms")]
    public static partial void Completed(ILogger logger, string outcome, int rows, int folds, long modelId, long totalMs);

    [LoggerMessage(EventId = 4571, Level = LogLevel.Warning, Message = "learning.policy.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4572, Level = LogLevel.Information, Message = "learning.policy.no_policy {Reason}")]
    public static partial void NoPolicy(ILogger logger, string reason);
}
