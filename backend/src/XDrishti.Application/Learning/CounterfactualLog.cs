using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the frozen-model counterfactual (P6-12; ids 4590 and 4591). Counts, codes and milliseconds only: never a trade.</summary>
internal static partial class CounterfactualLog
{
    [LoggerMessage(EventId = 4590, Level = LogLevel.Information, Message = "learning.counterfactual.completed {Outcome}, {Windows} windows, {Rows} judged rows, total {TotalMs} ms")]
    public static partial void Completed(ILogger logger, string outcome, int windows, int rows, long totalMs);

    [LoggerMessage(EventId = 4591, Level = LogLevel.Warning, Message = "learning.counterfactual.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
