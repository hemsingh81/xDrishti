using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the hypothesis loop (P6-10; ids 4580 to 4584). Ids, state codes, counts and error codes only: never a knob value or a rationale.</summary>
internal static partial class HypothesisLog
{
    [LoggerMessage(EventId = 4580, Level = LogLevel.Information, Message = "learning.hypothesis.proposed {HypothesisId}: {Source}")]
    public static partial void Proposed(ILogger logger, long hypothesisId, string source);

    [LoggerMessage(EventId = 4581, Level = LogLevel.Information, Message = "learning.hypothesis.moved {HypothesisId}: {From} to {To}")]
    public static partial void Moved(ILogger logger, long hypothesisId, string from, string to);

    [LoggerMessage(EventId = 4582, Level = LogLevel.Information, Message = "learning.hypothesis.judged {HypothesisId}: experiment {ExperimentId}, {Outcome}")]
    public static partial void Judged(ILogger logger, long hypothesisId, long experimentId, string outcome);

    [LoggerMessage(EventId = 4583, Level = LogLevel.Warning, Message = "learning.hypothesis.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4584, Level = LogLevel.Information, Message = "learning.hypothesis.triggers {Count} proposals, {Open} open")]
    public static partial void Triggers(ILogger logger, int count, int open);
}
