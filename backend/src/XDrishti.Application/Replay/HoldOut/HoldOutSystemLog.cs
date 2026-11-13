using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>Log lines of the system replay over a hold-out block (ids and type names only, no figure of the evidence).</summary>
internal static partial class HoldOutSystemLog
{
    [LoggerMessage(EventId = 8401, Level = LogLevel.Warning, Message = "The system replay of registration {RegistrationId} failed ({Failure}); the result is stored as run_failed")]
    public static partial void RunFailed(ILogger logger, Guid registrationId, string failure);

    [LoggerMessage(EventId = 8402, Level = LogLevel.Warning, Message = "The system replay loop failed a pass ({Failure}); it tries again later")]
    public static partial void LoopFailed(ILogger logger, string failure);

    [LoggerMessage(EventId = 8403, Level = LogLevel.Information, Message = "The system replay over a hold-out block stored {Count} result(s)")]
    public static partial void Stored(ILogger logger, int count);

    [LoggerMessage(EventId = 8404, Level = LogLevel.Warning, Message = "The system replay of registration {RegistrationId} hit {Failure} (try {Attempt}); the claim stays and the next pass tries again")]
    public static partial void RunRetry(ILogger logger, Guid registrationId, string failure, int attempt);

    [LoggerMessage(EventId = 8405, Level = LogLevel.Warning, Message = "The stored block of registration {RegistrationId} no longer ends where the registration said; nothing was read or stored")]
    public static partial void BlockChanged(ILogger logger, Guid registrationId);
}
