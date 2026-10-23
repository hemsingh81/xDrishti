using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the lessons (P6-16; ids 4600 to 4604). Ids, state codes, counts and error codes only: never a problem text or a reason.</summary>
internal static partial class LessonLog
{
    [LoggerMessage(EventId = 4600, Level = LogLevel.Information, Message = "learning.lesson.opened {LessonId}: {Monitor}")]
    public static partial void Opened(ILogger logger, long lessonId, string monitor);

    [LoggerMessage(EventId = 4601, Level = LogLevel.Information, Message = "learning.lesson.moved {LessonId}: {From} to {To}")]
    public static partial void Moved(ILogger logger, long lessonId, string from, string to);

    [LoggerMessage(EventId = 4602, Level = LogLevel.Warning, Message = "learning.lesson.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4603, Level = LogLevel.Information, Message = "learning.lesson.scanned {Findings} findings, {Opened} opened, {Existing} already recorded, dry run {DryRun}")]
    public static partial void Scanned(ILogger logger, int findings, int opened, int existing, bool dryRun);

    [LoggerMessage(EventId = 4604, Level = LogLevel.Information, Message = "learning.lesson.closed {LessonId}: experiment {ExperimentId}, {Result}")]
    public static partial void Closed(ILogger logger, long lessonId, long experimentId, string result);
}
