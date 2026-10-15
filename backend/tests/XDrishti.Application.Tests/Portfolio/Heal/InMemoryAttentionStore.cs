using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>The memory of the attention items with the rules of the real store: a known item keeps when it was first seen, one that holds again after being resolved starts anew, what no longer holds is resolved and kept for the given time.</summary>
internal sealed class InMemoryAttentionStore : IAttentionStore
{
    public Dictionary<string, StoredAttention> Items { get; } = [];

    public int Reconciliations { get; private set; }

    public ValueTask<IReadOnlyList<StoredAttention>> ReconcileAsync(IReadOnlyList<AttentionDraft> current, DateTimeOffset now, TimeSpan keepResolved, CancellationToken cancellationToken)
    {
        Reconciliations++;
        foreach (var draft in current)
        {
            Items[draft.Id] = Items.TryGetValue(draft.Id, out var known)
                ? known with { Kind = draft.Kind, Severity = draft.Severity, Title = draft.Title, Text = draft.Text, ActionsJson = draft.ActionsJson, AccountId = draft.AccountId, Since = known.ResolvedAt is null ? known.Since : now, ResolvedAt = null }
                : new StoredAttention(draft.Id, draft.Kind, draft.Severity, draft.Title, draft.Text, draft.ActionsJson, now, null, draft.AccountId);
        }

        var ids = current.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in Items.Values.Where(i => i.ResolvedAt is null && !ids.Contains(i.Id)).ToList())
        {
            Items[gone.Id] = gone with { ResolvedAt = now };
        }

        return ValueTask.FromResult<IReadOnlyList<StoredAttention>>([.. Items.Values.Where(i => i.ResolvedAt is null || i.ResolvedAt > now - keepResolved)]);
    }
}
