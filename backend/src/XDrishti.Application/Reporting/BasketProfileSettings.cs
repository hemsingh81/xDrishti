using System.ComponentModel.DataAnnotations;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// Settings of the basket profile (configuration section <c>Reports:BasketProfile</c>, BK-07, ADR 0262). They are the minimum samples of the pure engine (<see cref="BasketProfileOptions"/>, ADR 0257) and the limits of one read; the discontinuity threshold is
/// not repeated here: it is the one of <see cref="ReportBenchmarkOptions"/>.
/// </summary>
public sealed class BasketProfileSettings
{
    public const string SectionName = "Reports:BasketProfile";

    /// <summary>Fewest voters for a breadth or dispersion figure.</summary>
    [Range(2, 100)]
    public int MinVoters { get; set; } = 5;

    /// <summary>Fewest days for a regime row to be a number.</summary>
    [Range(5, 500)]
    public int MinDaysPerRegime { get; set; } = 30;

    /// <summary>Share of a member's ADV20 counted as its capacity (0.01 = 1 %).</summary>
    [Range(0.0001, 0.5)]
    public double CapacityPct { get; set; } = 0.01;

    /// <summary>Most members one basket may hold for a profile.</summary>
    [Range(5, 2000)]
    public int MaxInstruments { get; set; } = 300;

    /// <summary>Most baskets one ranking call may name.</summary>
    [Range(2, 50)]
    public int MaxBaskets { get; set; } = 12;

    /// <summary>Sessions of the default window, ending at the newest stored daily bar.</summary>
    [Range(20, 2000)]
    public int DefaultWindowSessions { get; set; } = 252;

    /// <summary>Most cached profiles.</summary>
    [Range(1, 1000)]
    public int CacheEntries { get; set; } = 64;

    /// <summary>The engine options: these settings and the discontinuity threshold of the benchmarks (one definition).</summary>
    public BasketProfileOptions ToOptions(double discontinuityThreshold) => new()
    {
        MinVoters = MinVoters,
        MinDaysPerRegime = MinDaysPerRegime,
        CapacityPct = CapacityPct,
        DiscontinuityThreshold = discontinuityThreshold,
    };
}
