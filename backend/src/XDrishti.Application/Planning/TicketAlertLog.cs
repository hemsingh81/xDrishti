using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the ticket alerts (P7-20; ids 4890 to 4893). Ids, kinds and counts only: never a price, a symbol, an e-mail or a note.</summary>
internal static partial class TicketAlertLog
{
    [LoggerMessage(EventId = 4890, Level = LogLevel.Information, Message = "plans.alert.crossed {ItemId}: {Kind}")]
    public static partial void Crossed(ILogger logger, long itemId, string kind);

    [LoggerMessage(EventId = 4891, Level = LogLevel.Debug, Message = "plans.alert.stored {Count} crossing(s)")]
    public static partial void Stored(ILogger logger, int count);

    [LoggerMessage(EventId = 4892, Level = LogLevel.Warning, Message = "plans.alert.observe_failed {ExceptionType}")]
    public static partial void ObserveFailed(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 4893, Level = LogLevel.Information, Message = "plans.prompts.stored {Count} prompt(s) for {Sessions} session(s)")]
    public static partial void PromptsStored(ILogger logger, int count, int sessions);
}
