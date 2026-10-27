namespace XDrishti.Application.Abstractions.Ai;

/// <summary>Reads the stored turns (spec P9-03 AC 10). Nothing here edits or deletes.</summary>
public interface IConversationReader
{
    ValueTask<IReadOnlyList<ConversationSummary>> ListAsync(int limit, CancellationToken cancellationToken);

    ValueTask<ConversationDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    ValueTask<StoredToolCall?> GetToolCallAsync(long id, CancellationToken cancellationToken);
}
