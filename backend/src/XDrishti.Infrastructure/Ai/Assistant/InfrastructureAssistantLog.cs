using Microsoft.Extensions.Logging;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>Log events of the assistant's infrastructure (spec P9-03; ids 6060 to 6119 are shared with the application part). Never an address, token, argument, result or exception text.</summary>
internal static partial class InfrastructureAssistantLog
{
    [LoggerMessage(EventId = 6065, Level = LogLevel.Warning, Message = "assistant.tools_unreachable {Operation} {ExceptionType}")]
    public static partial void ToolServerUnreachable(this ILogger logger, string operation, string exceptionType);
}
