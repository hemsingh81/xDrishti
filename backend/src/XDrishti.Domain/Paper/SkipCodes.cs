namespace XDrishti.Domain.Paper;

/// <summary>The final facts stored as a skip instead of an entry (spec P10-01, criterion 7). Stored by name, never renamed.</summary>
public static class SkipCodes
{
    /// <summary>The replay took no entry for the ticket.</summary>
    public const string NotTriggered = "not_triggered";

    /// <summary>The ticket cannot be replayed for a final reason (its detail says why).</summary>
    public const string NotGradable = "not_gradable";

    /// <summary>The replay dropped the trade by its sizing or cost rule.</summary>
    public const string DroppedBySizing = "dropped_by_sizing";

    /// <summary>A trade with several legs (partial exits, pyramids) is not captured.</summary>
    public const string LegsNotSupported = "legs_not_supported";

    /// <summary>The entry minute starts before the ticket was armed.</summary>
    public const string EnteredBeforeArmed = "entered_before_armed";

    /// <summary>The owner's stored size change cannot be read; the larger size is never used.</summary>
    public const string ModificationUnreadable = "modification_unreadable";

    /// <summary>An armed ticket has no stored approval row (a data fault, never guessed).</summary>
    public const string ApprovalMissing = "approval_missing";

    /// <summary>Every code, for the database check.</summary>
    public static IReadOnlyList<string> All { get; } = [NotTriggered, NotGradable, DroppedBySizing, LegsNotSupported, EnteredBeforeArmed, ModificationUnreadable, ApprovalMissing];
}
