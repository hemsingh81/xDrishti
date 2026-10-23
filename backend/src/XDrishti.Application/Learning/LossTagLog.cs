using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the loss tags (P6-15; ids 4530 and 4531). Counts, codes and milliseconds only: never a trade.</summary>
internal static partial class LossTagLog
{
    [LoggerMessage(EventId = 4530, Level = LogLevel.Information, Message = "learning.loss_tags.completed {Cells} cells, {Trades} trades, {Losses} losses, {Growing} growing, total {TotalMs} ms")]
    public static partial void Completed(ILogger logger, int cells, int trades, int losses, int growing, long totalMs);

    [LoggerMessage(EventId = 4531, Level = LogLevel.Warning, Message = "learning.loss_tags.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
