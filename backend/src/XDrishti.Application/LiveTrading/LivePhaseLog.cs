using Microsoft.Extensions.Logging;

namespace XDrishti.Application.LiveTrading;

/// <summary>Log event of the small-live phase check (id 5150): the date, the exit word and counts only; no price, quantity, symbol or account number.</summary>
internal static partial class LivePhaseLog
{
    [LoggerMessage(EventId = 5150, Level = LogLevel.Information, Message = "live.phase_check through={Through} exit={Exit} sessions={Sessions} met={Met} not_met={NotMet} ms={Ms}")]
    public static partial void Checked(ILogger logger, DateOnly through, string exit, int sessions, int met, int notMet, long ms);
}
