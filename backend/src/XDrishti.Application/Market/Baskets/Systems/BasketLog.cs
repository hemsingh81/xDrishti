using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Baskets;

/// <summary>Structured log events of the system baskets (BK-01, block 3500). Counts and outcomes only: never URLs or file content.</summary>
internal static partial class BasketLog
{
    [LoggerMessage(EventId = 3500, Level = LogLevel.Information, Message = "basket.fetch basket {BasketId}: {Outcome}, {Rows} rows, {Added} added, {Removed} removed, {Unmatched} unmatched, {Milliseconds} ms")]
    public static partial void Fetched(ILogger logger, Guid basketId, string outcome, int rows, int added, int removed, int unmatched, long milliseconds);

    [LoggerMessage(EventId = 3531, Level = LogLevel.Information, Message = "basket.list basket {BasketId}: {LotsWritten} lot rows written")]
    public static partial void LotsWritten(ILogger logger, Guid basketId, int lotsWritten);

    [LoggerMessage(EventId = 3530, Level = LogLevel.Information, Message = "basket.list basket {BasketId}: {Outcome}, {Members} members, {Unmatched} unmatched, {Milliseconds} ms")]
    public static partial void ListRefreshed(ILogger logger, Guid basketId, string outcome, int members, int unmatched, long milliseconds);
}
