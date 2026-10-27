namespace XDrishti.Application.Abstractions.Ai;

/// <summary>What is stored when a turn starts: the conversation (created when <paramref name="ConversationId"/> is null), the prompt (once per hash) and the user's message.</summary>
public sealed record StartTurnRecord(
    Guid? ConversationId, Guid TurnId, string Actor, string Origin, string Question, string PromptHash, string PromptName, string PromptVersion, string PromptText, long? SettingsId, string ModelId, string? LlmBuild);
