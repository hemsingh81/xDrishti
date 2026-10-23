using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the experiments (P6-11; ids 4540 to 4543). Ledger ids, counts, codes and milliseconds only: never a trade or a knob value.</summary>
internal static partial class ExperimentLog
{
    [LoggerMessage(EventId = 4540, Level = LogLevel.Information, Message = "learning.experiment.recorded {ExperimentId}: {Outcome}, {Trades} trades, {Folds} folds, {Trials} trials, compare {TotalMs} ms")]
    public static partial void Recorded(ILogger logger, long experimentId, string outcome, int trades, int folds, int trials, long totalMs);

    [LoggerMessage(EventId = 4541, Level = LogLevel.Information, Message = "learning.experiment.already_recorded {ExperimentId}")]
    public static partial void AlreadyRecorded(ILogger logger, long experimentId);

    [LoggerMessage(EventId = 4542, Level = LogLevel.Warning, Message = "learning.experiment.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4543, Level = LogLevel.Error, Message = "learning.experiment.failed")]
    public static partial void Failed(ILogger logger);
}
