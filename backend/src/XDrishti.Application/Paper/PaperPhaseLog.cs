using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Paper;

/// <summary>Structured log events of the paper phase check and the review's comparison line (P10-06; ids 5219 to 5224). Counts, ids and words only.</summary>
internal static partial class PaperPhaseLog
{
    [LoggerMessage(EventId = 5219, Level = LogLevel.Information, Message = "paper.phase epoch {EpochId} through {Through}: {Verdict}; C1 {C1}, C2 {C2}, C3 {C3}, C4 {C4}, I {Integrity}; {Counted} counted, {Day} days")]
    public static partial void Evaluated(ILogger logger, long epochId, DateOnly through, string verdict, string c1, string c2, string c3, string c4, string integrity, int counted, int day);

    [LoggerMessage(EventId = 5220, Level = LogLevel.Information, Message = "paper.phase refused: {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 5221, Level = LogLevel.Warning, Message = "paper.phase: the {Part} could not be read ({Code}); the check says so and goes on")]
    public static partial void PartUnavailable(ILogger logger, string part, string code);

    [LoggerMessage(EventId = 5222, Level = LogLevel.Warning, Message = "paper.phase epoch {EpochId}: malformed window, MET withheld")]
    public static partial void Malformed(ILogger logger, long epochId);

    [LoggerMessage(EventId = 5223, Level = LogLevel.Information, Message = "paper.review_comparison epoch {EpochId}: {State}")]
    public static partial void ComparisonLine(ILogger logger, long epochId, string state);
}
