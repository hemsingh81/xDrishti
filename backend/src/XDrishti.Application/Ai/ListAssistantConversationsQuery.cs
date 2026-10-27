using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Ai;

/// <summary>The newest conversations (1 to 50, default 20).</summary>
public sealed record ListAssistantConversationsQuery(int Limit = ListAssistantConversationsQuery.DefaultLimit) : IQuery<IReadOnlyList<ConversationSummary>>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
}
