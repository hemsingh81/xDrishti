using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Filings;

/// <summary>Log events 7030 to 7039 (P15-08): ids, states, counts and milliseconds only. Never an announcement's text or subject, the model's reason or its raw answer.</summary>
internal static partial class ExtractionLog
{
    [LoggerMessage(EventId = 7030, Level = LogLevel.Information, Message = "filings.extract.started from={From} to={To} limit={Limit}")]
    public static partial void Started(ILogger logger, DateOnly from, DateOnly to, int limit);

    [LoggerMessage(EventId = 7031, Level = LogLevel.Information, Message = "filings.extract.stored filing={FilingId} state={State} reason={Reason} ungrounded={Ungrounded} ms={Ms}")]
    public static partial void Stored(ILogger logger, Guid filingId, string state, string? reason, bool ungrounded, int ms);

    [LoggerMessage(EventId = 7032, Level = LogLevel.Warning, Message = "filings.extract.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 7033, Level = LogLevel.Warning, Message = "filings.extract.stopped reason={Reason} attempted={Attempted}")]
    public static partial void Stopped(ILogger logger, string reason, int attempted);

    [LoggerMessage(EventId = 7034, Level = LogLevel.Information, Message = "filings.extract.finished attempted={Attempted} ok={Ok} invalid={Invalid} unavailable={Unavailable} too_long={TooLong} no_text={NoText} ms={Ms}")]
    public static partial void Finished(ILogger logger, int attempted, int ok, int invalid, int unavailable, int tooLong, int noText, long ms);
}
