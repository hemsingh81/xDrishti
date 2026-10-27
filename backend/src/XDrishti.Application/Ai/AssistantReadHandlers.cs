using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>The conversation list (spec P9-03 AC 10).</summary>
internal sealed class ListAssistantConversationsHandler(IConversationReader reader) : IQueryHandler<ListAssistantConversationsQuery, IReadOnlyList<ConversationSummary>>
{
    public ValueTask<IReadOnlyList<ConversationSummary>> HandleAsync(ListAssistantConversationsQuery query, CancellationToken cancellationToken) =>
        reader.ListAsync(Math.Clamp(query.Limit, 1, ListAssistantConversationsQuery.MaxLimit), cancellationToken);
}

/// <summary>One conversation; 404 for an unknown id.</summary>
internal sealed class GetAssistantConversationHandler(IConversationReader reader) : IQueryHandler<GetAssistantConversationQuery, Result<ConversationDetail>>
{
    public async ValueTask<Result<ConversationDetail>> HandleAsync(GetAssistantConversationQuery query, CancellationToken cancellationToken) =>
        await reader.GetAsync(query.Id, cancellationToken) is { } detail
            ? Result.Success(detail)
            : Result.Failure<ConversationDetail>(Error.NotFound("assistant.not_found", "That conversation does not exist."));
}

/// <summary>One stored tool call; 404 for an unknown id.</summary>
internal sealed class GetAssistantToolCallHandler(IConversationReader reader) : IQueryHandler<GetAssistantToolCallQuery, Result<StoredToolCall>>
{
    public async ValueTask<Result<StoredToolCall>> HandleAsync(GetAssistantToolCallQuery query, CancellationToken cancellationToken) =>
        await reader.GetToolCallAsync(query.Id, cancellationToken) is { } call
            ? Result.Success(call)
            : Result.Failure<StoredToolCall>(Error.NotFound("assistant.not_found", "That tool call does not exist."));
}
