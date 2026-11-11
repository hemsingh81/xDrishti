using Microsoft.Extensions.Logging;

namespace XDrishti.Infrastructure.Planning;

/// <summary>Structured log event of the gate's breadth source (BK-08; id 7395, in the block 7390 to 7399). The basket, the row's state, whether a breadth was read and a reason code only: no figure of capital, no password.</summary>
internal static partial class GateSourceLog
{
    [LoggerMessage(EventId = 7395, Level = LogLevel.Information, Message = "gate.breadth_source basket {BasketId} for {Date}: row state {State}, available {Available}, reason {Reason}")]
    public static partial void Used(ILogger logger, Guid basketId, DateOnly date, string state, bool available, string reason);
}
