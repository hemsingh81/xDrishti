namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The rows a turn start created: the conversation and the user's message.</summary>
public sealed record StartedTurn(Guid ConversationId, long UserMessageId, int UserSeq);
