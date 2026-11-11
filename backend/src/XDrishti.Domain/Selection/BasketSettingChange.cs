namespace XDrishti.Domain.Selection;

/// <summary>
/// One new row of the basket settings history: the key, the value before and after, the direction, who made it and a hash of those. Only <see cref="BasketSettingChanges"/> makes one, and only for an owner actor.
/// </summary>
/// <param name="Key">The setting key.</param>
/// <param name="OldValue">The value before as text.</param>
/// <param name="NewValue">The value after as text.</param>
/// <param name="Direction"><c>tighten</c>, <c>loosen</c> or <c>neutral</c>.</param>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Hash">SHA-256 (hex) of the five fields above.</param>
public sealed record BasketSettingChange(string Key, string OldValue, string NewValue, string Direction, string Actor, string Hash)
{
    /// <summary>A change that can only make the plan or the gate stricter.</summary>
    public const string Tighten = "tighten";

    /// <summary>A change that can let more through (a different breadth source, a larger or removed cap, a higher cut).</summary>
    public const string Loosen = "loosen";

    /// <summary>A change that alters nothing in effect (the cut changed while the cap is OFF).</summary>
    public const string Neutral = "neutral";
}
