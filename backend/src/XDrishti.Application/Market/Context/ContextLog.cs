using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Context;

/// <summary>Structured log events of the basket upkeep after a data job (event id 3393: unique across the solution, asserted by the architecture tests).</summary>
internal static partial class ContextLog
{
    [LoggerMessage(EventId = 3393, Level = LogLevel.Error, Message = "Basket context upkeep after a data job failed")]
    public static partial void UpkeepCrashed(ILogger logger, Exception exception);
}
