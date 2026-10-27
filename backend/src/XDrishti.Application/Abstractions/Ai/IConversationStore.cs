namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// The audit of a chat turn (spec P9-03 AC 7, P9-00 D14): insert-only. A turn start stores the conversation (once), the prompt (once per hash) and the user's message; each tool call stores one row; the assistant's message is inserted once, at the end. Nothing is updated or deleted.
/// A crash mid-turn leaves the user's message and the calls without an assistant message, which the read shows as no answer stored.
/// </summary>
public interface IConversationStore
{
    /// <summary>Whether a conversation of this id exists.</summary>
    ValueTask<bool> ExistsAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>The last <paramref name="count"/> messages of the conversation, oldest first, each assistant message with the calls of its turn.</summary>
    ValueTask<IReadOnlyList<HistoryMessage>> RecentAsync(Guid conversationId, int count, CancellationToken cancellationToken);

    ValueTask<StartedTurn> StartTurnAsync(StartTurnRecord record, CancellationToken cancellationToken);

    ValueTask<long> RecordToolCallAsync(ToolCallRecord record, CancellationToken cancellationToken);

    /// <summary>Inserts the assistant's message and returns its id.</summary>
    ValueTask<long> CompleteTurnAsync(AssistantMessageRecord record, CancellationToken cancellationToken);
}
