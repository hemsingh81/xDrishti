namespace XDrishti.Domain.Portfolio;

/// <summary>
/// The owner's decision about one Action-center item, identified by the rule and the item key (the holding, or the account and sector for the concentration rule). Only decisions are stored (ADR 0022): the items themselves are
/// evaluated on read. Rows are never deleted (the runtime role has no DELETE): a decision that stops applying becomes <see cref="ActionDecision.Cleared"/> and is written again by the next decision on the same item.
/// </summary>
public sealed class ActionState
{
    public const int MaxRuleLength = 40;

    public const int MaxKeyLength = 100;

    public const int MaxUpdatedByLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private ActionState(string rule, string key, ActionDecision state, DateOnly? snoozedUntil, string updatedBy, DateTimeOffset updatedAt)
    {
        Rule = rule;
        Key = key;
        State = state;
        SnoozedUntil = snoozedUntil;
        UpdatedBy = updatedBy;
        UpdatedAt = updatedAt;
    }

    public string Rule { get; private set; }

    public string Key { get; private set; }

    public ActionDecision State { get; private set; }

    /// <summary>The India date the item shows again (set while <see cref="State"/> is <see cref="ActionDecision.Snoozed"/>).</summary>
    public DateOnly? SnoozedUntil { get; private set; }

    /// <summary>Who decided last (the signed-in owner's e-mail address, or <c>system:…</c> for a clearing).</summary>
    public string UpdatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ActionState Create(string rule, string key, ActionDecision state, DateOnly? snoozedUntil, string actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (rule.Length > MaxRuleLength || key.Length > MaxKeyLength)
        {
            throw new ArgumentException("The rule or the key is too long.");
        }

        return new ActionState(rule, key, state, Until(state, snoozedUntil), Limit(actor), now);
    }

    /// <summary>Records a decision; false (and nothing changed) when it is the one already stored, so repeating a decision is not a change.</summary>
    public bool Decide(ActionDecision state, DateOnly? snoozedUntil, string actor, DateTimeOffset now)
    {
        var until = Until(state, snoozedUntil);
        if (State == state && SnoozedUntil == until)
        {
            return false;
        }

        State = state;
        SnoozedUntil = until;
        UpdatedBy = Limit(actor);
        UpdatedAt = now;
        return true;
    }

    private static DateOnly? Until(ActionDecision state, DateOnly? snoozedUntil) => state == ActionDecision.Snoozed
        ? snoozedUntil ?? throw new ArgumentException("A snooze needs the date it ends.", nameof(snoozedUntil))
        : null;

    private static string Limit(string? actor)
    {
        var trimmed = actor?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "unknown" : trimmed.Length <= MaxUpdatedByLength ? trimmed : trimmed[..MaxUpdatedByLength];
    }
}
