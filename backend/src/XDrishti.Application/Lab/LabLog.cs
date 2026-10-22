using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Lab;

/// <summary>Structured log events of the Lab API (spec P5-01; event ids 4436 to 4438). Family id, version, hash prefix and counts only: never the text.</summary>
internal static partial class LabLog
{
    [LoggerMessage(EventId = 4436, Level = LogLevel.Information, Message = "lab.strategy.created strategy {StrategyId} version {Version} hash {Hash}")]
    public static partial void Created(ILogger logger, string strategyId, int version, string hash);

    [LoggerMessage(EventId = 4437, Level = LogLevel.Information, Message = "lab.strategy.versioned strategy {StrategyId} version {Version} hash {Hash}")]
    public static partial void Versioned(ILogger logger, string strategyId, int version, string hash);

    [LoggerMessage(EventId = 4438, Level = LogLevel.Information, Message = "lab.validate.rejected {Errors} error(s), {Warnings} warning(s)")]
    public static partial void Rejected(ILogger logger, int errors, int warnings);
}
