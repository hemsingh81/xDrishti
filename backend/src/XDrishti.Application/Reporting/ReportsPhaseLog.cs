using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Reporting;

/// <summary>Structured log events of the P8 phase check (spec P8-17; ids 5255 and 5256). Counts, words and milliseconds only.</summary>
internal static partial class ReportsPhaseLog
{
    [LoggerMessage(EventId = 5255, Level = LogLevel.Information, Message = "reports.phaseCheck.evaluated: {Met} met, {NotMet} not met, {NotEvaluable} not evaluable, {Ms} ms")]
    public static partial void Evaluated(ILogger logger, int met, int notMet, int notEvaluable, long ms);

    [LoggerMessage(EventId = 5256, Level = LogLevel.Warning, Message = "reports.phaseCheck: {Report} could not be read ({Code}); the check says so and goes on")]
    public static partial void Unreadable(ILogger logger, string report, string code);
}
