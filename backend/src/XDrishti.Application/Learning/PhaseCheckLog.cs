using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the phase check (P6-18; ids 4620 to 4623). Counts and milliseconds only.</summary>
internal static partial class PhaseCheckLog
{
    [LoggerMessage(EventId = 4620, Level = LogLevel.Information, Message = "learning.phase_check.evaluated met {Met}, not met {NotMet}, not evaluable {NotEvaluable}")]
    public static partial void Evaluated(ILogger logger, int met, int notMet, int notEvaluable);

    [LoggerMessage(EventId = 4621, Level = LogLevel.Information, Message = "learning.phase_check.compare_measured experiment {ExperimentId}: {Trades} trades in {Ms} ms")]
    public static partial void CompareMeasured(ILogger logger, long experimentId, int trades, long ms);

    [LoggerMessage(EventId = 4622, Level = LogLevel.Warning, Message = "learning.phase_check.compare_unavailable experiment {ExperimentId}: {Code}")]
    public static partial void CompareUnavailable(ILogger logger, long experimentId, string code);

    [LoggerMessage(EventId = 4623, Level = LogLevel.Warning, Message = "learning.phase_check.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
