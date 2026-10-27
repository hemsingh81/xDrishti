using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>Log events 6160 to 6169 (spec P9-06 AC 14). Ids, counts, finding codes and milliseconds only: never the text, a prompt or a figure.</summary>
internal static partial class BriefingLog
{
    [LoggerMessage(EventId = 6160, Level = LogLevel.Information, Message = "assistant.briefing.stored {Kind} {Subject} version={Version} source={Source} reason={Reason} words={Words} attempts={Attempts}")]
    public static partial void Stored(ILogger logger, string kind, string subject, int version, string source, string? reason, int words, int attempts);

    [LoggerMessage(EventId = 6161, Level = LogLevel.Information, Message = "assistant.briefing.attempt {Kind} {Subject} attempt={Attempt} passed={Passed} findings={Findings} codes={Codes} ms={Ms}")]
    public static partial void Attempt(ILogger logger, string kind, string subject, int attempt, bool passed, int findings, string codes, int ms);

    [LoggerMessage(EventId = 6162, Level = LogLevel.Information, Message = "assistant.briefing.repeat {Kind} {Subject} id={Id}")]
    public static partial void Repeat(ILogger logger, string kind, string subject, long id);

    [LoggerMessage(EventId = 6163, Level = LogLevel.Information, Message = "assistant.briefing.deferred {Kind} {Subject} why={Why}")]
    public static partial void Deferred(ILogger logger, string kind, string subject, string why);

    [LoggerMessage(EventId = 6164, Level = LogLevel.Warning, Message = "assistant.briefing.model_failed {Kind} {Subject} attempt={Attempt}")]
    public static partial void ModelFailed(ILogger logger, string kind, string subject, int attempt);

    [LoggerMessage(EventId = 6165, Level = LogLevel.Information, Message = "assistant.briefing.facts {Kind} {Subject} lines={Lines} tickets={Tickets} injectionFlags={Flags}")]
    public static partial void Facts(ILogger logger, string kind, string subject, int lines, int tickets, int flags);

    [LoggerMessage(EventId = 6166, Level = LogLevel.Information, Message = "assistant.briefing.dry_run {Kind} {Subject}")]
    public static partial void DryRun(ILogger logger, string kind, string subject);
}
