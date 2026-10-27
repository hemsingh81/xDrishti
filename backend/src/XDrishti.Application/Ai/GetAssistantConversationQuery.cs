using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>One conversation with its messages and their tool-call summaries.</summary>
public sealed record GetAssistantConversationQuery(Guid Id) : IQuery<Result<ConversationDetail>>;
