using System.ComponentModel.DataAnnotations;
using XDrishti.Domain.Market.Strength;

namespace XDrishti.Application.Market.Strength;

/// <summary>
/// Settings of the relative-strength step (configuration section <c>Market:RelativeStrength</c>, BK-09, ADR 0263). The step is OFF by default (a computed feature, but it reads every member's bars). The definitions themselves (lookbacks 126 and 252, skip 21, windows 21, 63 and 126, the
/// Nifty 50 and 20 sessions) are fixed in the domain and are not settings: a setting there would be a free parameter.
/// </summary>
public sealed class RelativeStrengthSettings
{
    public const string SectionName = "Market:RelativeStrength";

    /// <summary>Whether the daily step runs after the end-of-day aggregation.</summary>
    public bool Enabled { get; set; }

    /// <summary>Most missing sessions the daily step catches up in one run (idempotent: a day with rows is skipped).</summary>
    [Range(1, 60)]
    public int CatchUpSessions { get; set; } = 5;

    /// <summary>Fewest ranked members for a percent rank.</summary>
    [Range(2, 100)]
    public int MinVoters { get; set; } = 10;

    /// <summary>Fewest members with the full window for a sector figure.</summary>
    [Range(2, 100)]
    public int MinSectorMembers { get; set; } = 5;

    /// <summary>The domain options of these settings.</summary>
    public StrengthOptions ToOptions() => new() { MinVoters = MinVoters, MinSectorMembers = MinSectorMembers };
}
