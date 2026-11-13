namespace XDrishti.Domain.Lab;

/// <summary>
/// The rule of the auto release (HO-01 D4, ADR 0282): a sealed block is released by itself only when it has at least one fresh look and no look is still available for any kind. An unlooked block is never released automatically (that forfeit is the owner's), and an
/// <c>Unavailable</c> read keeps the block. Pure.
/// </summary>
public static class HoldOutAutoRelease
{
    /// <summary>The recorded actor of an automatic release.</summary>
    public const string Actor = "system:auto-release";

    /// <summary>Whether the block is released now; <paramref name="reason"/> names what holds it, or is empty.</summary>
    public static bool Eligible(HoldOutBlockState state, IReadOnlyCollection<LookAvailability> kinds, out string reason)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        reason = state switch
        {
            HoldOutBlockState.Released => "released",
            HoldOutBlockState.Filling => "filling",
            HoldOutBlockState.Sealed => "unlooked",
            _ when kinds.Contains(LookAvailability.Unavailable) => "unavailable",
            _ when kinds.Contains(LookAvailability.Available) => "look_available",
            _ => string.Empty,
        };
        return reason.Length == 0;
    }
}
