using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>Log events 6170 to 6179 (spec P9-07 AC 11). Ids, counts, codes and milliseconds only: never the idea, the text or an explanation.</summary>
internal static partial class DraftLog
{
    [LoggerMessage(EventId = 6170, Level = LogLevel.Information, Message = "assistant.draft.started id={Id}")]
    public static partial void Started(ILogger logger, long id);

    [LoggerMessage(EventId = 6171, Level = LogLevel.Information, Message = "assistant.draft.attempt id={Id} attempt={Attempt} status={Status} problems={Problems} findings={Findings} codes={Codes} ms={Ms}")]
    public static partial void Attempt(ILogger logger, long id, int attempt, string status, int problems, int findings, string codes, int ms);

    [LoggerMessage(EventId = 6172, Level = LogLevel.Information, Message = "assistant.draft.finished id={Id} status={Status} reason={Reason} attempts={Attempts}")]
    public static partial void Finished(ILogger logger, long id, string status, string? reason, int attempts);

    [LoggerMessage(EventId = 6173, Level = LogLevel.Warning, Message = "assistant.draft.model_failed id={Id} attempt={Attempt}")]
    public static partial void ModelFailed(ILogger logger, long id, int attempt);

    [LoggerMessage(EventId = 6174, Level = LogLevel.Information, Message = "assistant.draft.confirmed id={Id} strategy={Strategy} version={Version} run={Run} edited={Edited}")]
    public static partial void Confirmed(ILogger logger, long id, string strategy, int version, Guid? run, bool edited);

    [LoggerMessage(EventId = 6175, Level = LogLevel.Information, Message = "assistant.draft.discarded id={Id}")]
    public static partial void Discarded(ILogger logger, long id);

    [LoggerMessage(EventId = 6176, Level = LogLevel.Information, Message = "assistant.draft.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 6177, Level = LogLevel.Error, Message = "assistant.draft.job_crashed id={Id}")]
    public static partial void JobCrashed(ILogger logger, long id);
}
