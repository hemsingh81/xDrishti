using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>Log lines of the system replay over a hold-out block (ids and type names only, no figure of the evidence).</summary>
internal static partial class HoldOutSystemLog
{
    [LoggerMessage(EventId = 7301, Level = LogLevel.Warning, Message = "The system replay of registration {RegistrationId} failed ({Failure}); the result is stored as inconclusive")]
    public static partial void RunFailed(ILogger logger, Guid registrationId, string failure);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Warning, Message = "The system replay loop failed a pass ({Failure}); it tries again later")]
    public static partial void LoopFailed(ILogger logger, string failure);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Information, Message = "The system replay over a hold-out block stored {Count} result(s)")]
    public static partial void Stored(ILogger logger, int count);
}
