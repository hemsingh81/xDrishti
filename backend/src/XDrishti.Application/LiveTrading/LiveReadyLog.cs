using Microsoft.Extensions.Logging;

namespace XDrishti.Application.LiveTrading;

/// <summary>Structured log event of the live-ready gate of the account mode switch (id 5134, in the ramp range 5130 to 5139).</summary>
internal static partial class LiveReadyLog
{
    [LoggerMessage(EventId = 5134, Level = LogLevel.Warning, Message = "live.not_ready the switch to manual_live was refused: {Items}")]
    public static partial void NotReady(ILogger logger, string items);
}
