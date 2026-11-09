using Microsoft.Extensions.Logging;

namespace XDrishti.Application.LearnedSettings;

/// <summary>Structured log events of learned changes (AU-10; ids 5560 to 5565). Keys, ids and codes only.</summary>
internal static partial class LearnedLog
{
    [LoggerMessage(EventId = 5560, Level = LogLevel.Information, Message = "learned.proposed {RowId} {Key} {Direction}")]
    public static partial void Proposed(ILogger logger, long rowId, string key, string direction);

    [LoggerMessage(EventId = 5561, Level = LogLevel.Information, Message = "learned.decided {RowId} {State} by {Who}")]
    public static partial void Decided(ILogger logger, long rowId, string state, string who);

    [LoggerMessage(EventId = 5562, Level = LogLevel.Information, Message = "learned.skipped {Key} {Code}")]
    public static partial void Skipped(ILogger logger, string key, string code);

    [LoggerMessage(EventId = 5563, Level = LogLevel.Warning, Message = "learned.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
