using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the decision-time features and the training set (P6-04; ids 4480 to 4485). Keys (first 12 characters), counts, codes and milliseconds only: never a price or a trade.</summary>
internal static partial class FeatureLog
{
    [LoggerMessage(EventId = 4480, Level = LogLevel.Information, Message = "learning.features.completed run {RunKey}: {Trades} trades, {Stored} new rows, {NoBars} without bars, total {TotalMs} ms")]
    public static partial void Extracted(ILogger logger, string runKey, int trades, int stored, int noBars, long totalMs);

    [LoggerMessage(EventId = 4481, Level = LogLevel.Warning, Message = "learning.features.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4482, Level = LogLevel.Error, Message = "learning.features.failed run {RunKey}")]
    public static partial void Failed(ILogger logger, string runKey);

    [LoggerMessage(EventId = 4483, Level = LogLevel.Information, Message = "learning.training_set.completed {Id}: {Rows} rows ({Positives} positive), {DroppedNoFeatures} without features, {DroppedUnknown} with an unknown feature, {Outcome}, total {TotalMs} ms")]
    public static partial void TrainingSetBuilt(ILogger logger, string id, int rows, int positives, int droppedNoFeatures, int droppedUnknown, string outcome, long totalMs);

    [LoggerMessage(EventId = 4484, Level = LogLevel.Warning, Message = "learning.training_set.refused {Code}")]
    public static partial void TrainingSetRefused(ILogger logger, string code);

    [LoggerMessage(EventId = 4485, Level = LogLevel.Error, Message = "learning.training_set.failed")]
    public static partial void TrainingSetFailed(ILogger logger);
}
