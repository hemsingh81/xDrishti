using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Baskets;

/// <summary>Structured log events of the derived baskets (BK-03, block 3520). Counts and outcomes only: never an instrument list.</summary>
internal static partial class DerivedLog
{
    [LoggerMessage(EventId = 3520, Level = LogLevel.Information, Message = "basket.derived basket {BasketId} as of {AsOf}: {Outcome}, {Members} members, {Universe} in the universe, {Excluded} excluded, {Milliseconds} ms")]
    public static partial void Formed(ILogger logger, Guid basketId, DateOnly asOf, string outcome, int members, int universe, int excluded, long milliseconds);
}
