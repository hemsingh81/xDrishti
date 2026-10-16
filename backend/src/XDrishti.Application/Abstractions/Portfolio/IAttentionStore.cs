namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>An item as the derivation produced it, ready to be remembered: <c>ActionsJson</c> is the actions as JSON.</summary>
public sealed record AttentionDraft(string Id, string Kind, string Severity, string Title, string Text, string ActionsJson, Guid? AccountId);

/// <summary>An item as remembered: <c>Since</c> is when it was first seen in this situation, <c>ResolvedAt</c> when its condition stopped holding (null while it holds).</summary>
public sealed record StoredAttention(string Id, string Kind, string Severity, string Title, string Text, string ActionsJson, DateTimeOffset Since, DateTimeOffset? ResolvedAt, Guid? AccountId);

/// <summary>
/// The memory of the attention items (P2-16, ADR 0027, ADR 0030; <c>portfolio.attention_items</c>). Writing and reading are apart: <see cref="ReconcileAsync"/> is called by the reconciler (the healer tick, the end of a sync run), <see cref="ListAsync"/> by the notices, which
/// only read. An item that stopped holding is marked resolved and is not returned after the time it is kept; resolved rows are deleted by <see cref="DeleteResolvedBeforeAsync"/> after the retention (the one DELETE of the runtime role in the portfolio schema).
/// </summary>
public interface IAttentionStore
{
    /// <summary>
    /// Remembers <paramref name="current"/> (new ones with <c>Since = now</c>, known ones keep their <c>Since</c> and take the new text; one that was resolved and holds again starts anew) and marks every remembered item that is not among them as resolved at
    /// <paramref name="now"/>, in one statement however many items there are.
    /// </summary>
    ValueTask ReconcileAsync(IReadOnlyList<AttentionDraft> current, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Reads, without writing, the unresolved items and those resolved less than <paramref name="keepResolved"/> before <paramref name="now"/>.</summary>
    ValueTask<IReadOnlyList<StoredAttention>> ListAsync(DateTimeOffset now, TimeSpan keepResolved, CancellationToken cancellationToken);

    /// <summary>Deletes, in one statement, the items that were resolved before <paramref name="cutoff"/>; unresolved items are never deleted. Returns how many.</summary>
    ValueTask<int> DeleteResolvedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
