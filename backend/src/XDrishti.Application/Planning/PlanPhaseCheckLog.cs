using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the P7 phase check (P7-18; ids 4790 to 4799). Dates and counts only.</summary>
internal static partial class PlanPhaseCheckLog
{
    [LoggerMessage(EventId = 4790, Level = LogLevel.Information, Message = "plan.phase_check.evaluated through {Through}: exit {Exit}, streak {Streak}, met {Met}, not met {NotMet}, not evaluable {NotEvaluable}")]
    public static partial void Evaluated(ILogger logger, DateOnly through, string exit, int streak, int met, int notMet, int notEvaluable);

    [LoggerMessage(EventId = 4791, Level = LogLevel.Warning, Message = "plan.phase_check.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
