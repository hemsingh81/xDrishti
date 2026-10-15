namespace XDrishti.Domain.Portfolio;

/// <summary>
/// An item that needs the owner's attention, as it was last seen (P2-16, ADR 0027; <c>portfolio.attention_items</c>). The items are derived on read from the state of the accounts and syncs; this row only remembers each one so that
/// <see cref="Since"/> stays when it was first seen and a resolved item can be shown for a day (<see cref="ResolvedAt"/>) with the title, text and actions it had. The id includes the situation, so a changed situation is a new item. The
/// store writes it with set-based statements (insert-or-update, resolve); the entity is for reading. Resolved rows are deleted after the retention (ADR 0030); the runtime role has DELETE on this table only.
/// </summary>
public sealed class AttentionRecord
{
    public const int MaxIdLength = 200;

    public const int MaxTitleLength = 160;

    public const int MaxTextLength = 500;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private AttentionRecord(string id, string kind, string severity, string title, string text, string actions, DateTimeOffset since, DateTimeOffset updatedAt)
    {
        Id = id;
        Kind = kind;
        Severity = severity;
        Title = title;
        Text = text;
        Actions = actions;
        Since = since;
        UpdatedAt = updatedAt;
    }

    public string Id { get; private set; }

    public string Kind { get; private set; }

    /// <summary><c>error</c>, <c>warning</c> or <c>info</c>.</summary>
    public string Severity { get; private set; }

    public string Title { get; private set; }

    public string Text { get; private set; }

    /// <summary>The actions as JSON (at most two); written and read by the Application layer.</summary>
    public string Actions { get; private set; }

    /// <summary>When the item was first seen in this situation.</summary>
    public DateTimeOffset Since { get; private set; }

    /// <summary>When the condition stopped holding; null while it holds.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>The account the item is about; null for an item that is not about one.</summary>
    public Guid? AccountId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
