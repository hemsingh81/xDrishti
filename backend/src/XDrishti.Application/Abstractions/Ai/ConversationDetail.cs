namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A conversation with its messages in order.</summary>
public sealed record ConversationDetail(Guid Id, string Title, string Origin, DateTimeOffset CreatedAt, string ModelId, long? SettingsId, IReadOnlyList<MessageView> Messages);
