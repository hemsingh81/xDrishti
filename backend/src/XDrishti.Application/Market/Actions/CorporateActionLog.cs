using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Actions;

/// <summary>Structured log events of the corporate actions (event ids 3381 to 3389: unique across the solution, asserted by the architecture tests). Symbols and counts only.</summary>
internal static partial class CorporateActionLog
{
    [LoggerMessage(EventId = 3381, Level = LogLevel.Information, Message = "Corporate actions: {Suggested} new suggestion(s) for {Symbol}")]
    public static partial void Suggested(ILogger logger, string symbol, int suggested);

    [LoggerMessage(EventId = 3382, Level = LogLevel.Error, Message = "Corporate actions: the detection for {Symbol} failed")]
    public static partial void DetectionFailed(ILogger logger, string symbol, Exception exception);

    [LoggerMessage(EventId = 3383, Level = LogLevel.Information, Message = "Corporate actions: detection over {Instruments} instrument(s): {Candidates} candidate(s), {Suggested} new suggestion(s)")]
    public static partial void DetectionRun(ILogger logger, int instruments, int candidates, int suggested);
}
