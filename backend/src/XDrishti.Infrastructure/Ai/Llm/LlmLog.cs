using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Llm;

/// <summary>Log events 6000 to 6019 (spec P9-01). Never the address of the server, the key or an exception message.</summary>
internal static partial class LlmLog
{
    [LoggerMessage(EventId = 6000, Level = LogLevel.Debug, Message = "llm.status {State}")]
    public static partial void Status(this ILogger logger, LlmState state);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Error, Message = "llm.key_missing: the model server is enabled but Assistant:Llm:ApiKey is empty")]
    public static partial void KeyMissing(this ILogger logger);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Warning, Message = "llm.status_failed {ExceptionType}")]
    public static partial void StatusFailed(this ILogger logger, string exceptionType);
}
