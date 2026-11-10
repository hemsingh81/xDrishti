namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>
/// One new row of a sizing history (a setting key, or a basket budget as <c>budget:{basketId}</c>): the value before and after, the direction, who made it and a hash of those. Only <see cref="SizingChanges"/> makes one, and only for an owner actor.
/// </summary>
/// <param name="Key">The setting key, or the budget key.</param>
/// <param name="OldValue">The value before as text; <c>none</c> for a budget that did not exist or was removed.</param>
/// <param name="NewValue">The value after as text; <c>none</c> for a removed budget.</param>
/// <param name="Direction"><c>tighten</c>, <c>loosen</c> or <c>neutral</c>.</param>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Hash">SHA-256 (hex) of the five fields above.</param>
public sealed record SizingChange(string Key, string OldValue, string NewValue, string Direction, string Actor, string Hash)
{
    /// <summary>A change that can only make a suggestion smaller or a limit stricter.</summary>
    public const string Tighten = "tighten";

    /// <summary>A change that can make a suggestion larger or remove a limit; shown in the next plan until it is dismissed.</summary>
    public const string Loosen = "loosen";

    /// <summary>No change in effect.</summary>
    public const string Neutral = "neutral";

    /// <summary>The text of a value that is absent.</summary>
    public const string None = "none";
}
