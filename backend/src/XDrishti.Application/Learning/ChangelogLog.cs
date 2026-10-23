using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the learning changelog (P6-08; ids 4550 to 4553). Row ids, state codes and error codes only.</summary>
internal static partial class ChangelogLog
{
    [LoggerMessage(EventId = 4550, Level = LogLevel.Information, Message = "learning.changelog.transition {RowId}: {From} to {To}")]
    public static partial void Transition(ILogger logger, long rowId, string from, string to);

    [LoggerMessage(EventId = 4551, Level = LogLevel.Information, Message = "learning.changelog.revert {RowId} reverts {RevertedId}")]
    public static partial void Reverted(ILogger logger, long rowId, long revertedId);

    [LoggerMessage(EventId = 4552, Level = LogLevel.Warning, Message = "learning.changelog.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
