namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A line of the conversation list (newest first).</summary>
public sealed record ConversationSummary(Guid Id, string Title, string Origin, DateTimeOffset CreatedAt, int MessageCount, DateTimeOffset LastActivityAt);
