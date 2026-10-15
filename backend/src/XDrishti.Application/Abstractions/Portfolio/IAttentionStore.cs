namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>An item as the derivation produced it, ready to be remembered: <c>ActionsJson</c> is the actions as JSON.</summary>
public sealed record AttentionDraft(string Id, string Kind, string Severity, string Title, string Text, string ActionsJson, Guid? AccountId);

/// <summary>An item as remembered: <c>Since</c> is when it was first seen in this situation, <c>ResolvedAt</c> when its condition stopped holding (null while it holds).</summary>
public sealed record StoredAttention(string Id, string Kind, string Severity, string Title, string Text, string ActionsJson, DateTimeOffset Since, DateTimeOffset? ResolvedAt, Guid? AccountId);

/// <summary>
/// The memory of the attention items (P2-16, ADR 0027; <c>portfolio.attention_items</c>): one reconciliation per read of the notices, set-based. Items are never deleted: an item that stopped holding is marked resolved and is simply not returned after the time it is kept.
/// </summary>
public interface IAttentionStore
{
    /// <summary>
    /// Remembers <paramref name="current"/> (new ones with <c>Since = now</c>, known ones keep their <c>Since</c> and take the new text; one that was resolved and holds again starts anew), marks every remembered item that is not among them as resolved at <paramref name="now"/>,
    /// and returns the unresolved items and those resolved less than <paramref name="keepResolved"/> ago.
    /// </summary>
    ValueTask<IReadOnlyList<StoredAttention>> ReconcileAsync(IReadOnlyList<AttentionDraft> current, DateTimeOffset now, TimeSpan keepResolved, CancellationToken cancellationToken);
}
