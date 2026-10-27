using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Ai;

/// <summary>Log events 6060 to 6119 (spec P9-03). Ids, counts, codes and statuses only: never a message, prompt, argument, result or exception text.</summary>
internal static partial class AssistantLog
{
    [LoggerMessage(EventId = 6060, Level = LogLevel.Information, Message = "assistant.turn.start {ConversationId} {TurnId} history={History} tools={Tools}")]
    public static partial void TurnStart(ILogger logger, Guid conversationId, Guid turnId, int history, int tools);

    [LoggerMessage(EventId = 6061, Level = LogLevel.Information, Message = "assistant.turn.end {TurnId} finish={Finish} grounding={Grounding} checked={Checked} ungrounded={Ungrounded} calls={Calls} promptTokens={PromptTokens} completionTokens={CompletionTokens} ms={Ms}")]
    public static partial void TurnEnd(ILogger logger, Guid turnId, string finish, string grounding, int @checked, int ungrounded, int calls, int? promptTokens, int? completionTokens, int ms);

    [LoggerMessage(EventId = 6062, Level = LogLevel.Information, Message = "assistant.turn.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 6063, Level = LogLevel.Information, Message = "assistant.tool {Tool} status={Status} ms={Ms}")]
    public static partial void ToolCall(ILogger logger, string tool, string status, int ms);

    [LoggerMessage(EventId = 6064, Level = LogLevel.Information, Message = "assistant.settings.changed {SettingsId}")]
    public static partial void SettingsChanged(ILogger logger, long settingsId);

    [LoggerMessage(EventId = 6066, Level = LogLevel.Information, Message = "assistant.turn.cancelled {TurnId}")]
    public static partial void Cancelled(ILogger logger, Guid turnId);

    [LoggerMessage(EventId = 6123, Level = LogLevel.Information, Message = "assistant.chart_rejected {Reason}")]
    public static partial void ChartRejected(ILogger logger, string reason);
}
