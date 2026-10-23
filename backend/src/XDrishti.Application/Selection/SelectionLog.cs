using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Selection;

/// <summary>Structured log events of selection (P7-01; ids 4640 to 4649). Counts and codes only.</summary>
internal static partial class SelectionLog
{
    [LoggerMessage(EventId = 4640, Level = LogLevel.Information, Message = "selection.shown date {Date}: {Eligible} eligible, {Rejected} rejected, {Held} held of {Cells} cells")]
    public static partial void Shown(ILogger logger, DateOnly date, int eligible, int rejected, int held, int cells);

    [LoggerMessage(EventId = 4641, Level = LogLevel.Warning, Message = "selection.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
