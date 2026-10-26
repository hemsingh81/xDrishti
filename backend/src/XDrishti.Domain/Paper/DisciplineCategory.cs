namespace XDrishti.Domain.Paper;

/// <summary>The categories of a primary ticket in the discipline report (spec P10-03, criterion 3). Stored and shown by name; never renamed. Every primary ticket falls in exactly one of the seven.</summary>
public static class DisciplineCategory
{
    /// <summary>Approved at full size, entered and closed.</summary>
    public const string Followed = "followed";

    /// <summary>Approved below the proposed size, entered and closed.</summary>
    public const string SizedDown = "sized_down";

    /// <summary>Approved at any size, but no entry: the trigger never came (the skip code says why).</summary>
    public const string ApprovedNotEntered = "approved_not_entered";

    /// <summary>Not decided yet, or approved and waiting for the capture, or entered and still open.</summary>
    public const string Pending = "pending";

    /// <summary>The owner rejected it.</summary>
    public const string Rejected = "rejected";

    /// <summary>Not reviewed by the cutoff.</summary>
    public const string Lapsed = "lapsed";

    /// <summary>The system dropped it at the pre-open check (not the owner's act).</summary>
    public const string InvalidatedPreopen = "invalidated_preopen";

    /// <summary>The seven, in the order they are shown.</summary>
    public static IReadOnlyList<string> All { get; } = [Followed, SizedDown, ApprovedNotEntered, Pending, Rejected, Lapsed, InvalidatedPreopen];
}
