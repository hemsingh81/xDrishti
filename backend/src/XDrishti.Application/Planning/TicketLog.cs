using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the ticket builder (P7-04; ids 4700 to 4709). Counts and codes only: no price, no secret.</summary>
internal static partial class TicketLog
{
    [LoggerMessage(EventId = 4700, Level = LogLevel.Information, Message = "tickets.built {ForDate}: {Conditional} conditional, {Rejected} rejected at the close, {Unavailable} unavailable; plan {Primary} primary, {Reserve} reserve, {NotChosen} not chosen")]
    public static partial void Built(ILogger logger, DateOnly forDate, int conditional, int rejected, int unavailable, int primary, int reserve, int notChosen);

    [LoggerMessage(EventId = 4701, Level = LogLevel.Warning, Message = "tickets.source_failed {ForDate}: {Code}")]
    public static partial void SourceFailed(ILogger logger, DateOnly forDate, string code);

    [LoggerMessage(EventId = 4702, Level = LogLevel.Information, Message = "tickets.unavailable {Code}: {Count}")]
    public static partial void UnavailableCode(ILogger logger, string code, int count);
}
