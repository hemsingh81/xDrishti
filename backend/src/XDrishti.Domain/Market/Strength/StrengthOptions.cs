namespace XDrishti.Domain.Market.Strength;

/// <summary>The fixed definitions of BK-09 and the minimum samples. The momentum lookbacks and the skip are pre-registered and are not tuned; the minimums are researched defaults the caller may bind from settings.</summary>
public sealed record StrengthOptions
{
    /// <summary>The pre-registered skip of the latest month, in sessions.</summary>
    public const int Skip = 21;

    /// <summary>The only index-relative window in sessions.</summary>
    public const int IndexWindow = 20;

    /// <summary>The pre-registered momentum lookbacks in sessions (6 and 12 months).</summary>
    public static IReadOnlyList<int> Lookbacks { get; } = [126, 252];

    /// <summary>The pre-registered sector windows in sessions.</summary>
    public static IReadOnlyList<int> SectorWindows { get; } = [21, 63, 126];

    /// <summary>Fewest ranked members for a percent rank.</summary>
    public int MinVoters { get; init; } = 10;

    /// <summary>Fewest members with the full window for a sector figure.</summary>
    public int MinSectorMembers { get; init; } = 5;
}
