namespace XDrishti.Domain.Reporting;

/// <summary>
/// The minimum samples and rules of the basket profile (BK-06 Decision 8). The values here are the researched defaults; the caller binds them from settings and may replace any of them. Windows below 20 exist for tests.
/// </summary>
public sealed record BasketProfileOptions
{
    /// <summary>Fewest voters for a breadth or dispersion figure.</summary>
    public int MinVoters { get; init; } = 5;

    /// <summary>Fewest days for a regime row to be a number.</summary>
    public int MinDaysPerRegime { get; init; } = 30;

    /// <summary>Fewest members for a day return, a hold return and a contribution.</summary>
    public int MinMembersForReturn { get; init; } = 3;

    /// <summary>Largest one-day move kept (0.25 = 25 %, the P8-07 setting); a larger one removes that instrument-day.</summary>
    public double DiscontinuityThreshold { get; init; } = 0.25;

    /// <summary>Share of a member's ADV20 counted as its capacity.</summary>
    public double CapacityPct { get; init; } = 0.01;

    /// <summary>Sessions of returns in the average pairwise correlation.</summary>
    public int CorrelationWindow { get; init; } = 60;

    /// <summary>Fewest common sessions for a pair.</summary>
    public int MinCommonSessions { get; init; } = 40;

    /// <summary>Most members in the correlation; more are cut to the most liquid.</summary>
    public int MaxCorrelationMembers { get; init; } = 200;

    /// <summary>Fewest members for a correlation figure.</summary>
    public int MinCorrelationMembers { get; init; } = 5;

    /// <summary>Fewest pairs for a correlation figure.</summary>
    public int MinCorrelationPairs { get; init; } = 10;

    /// <summary>Volatility windows (index returns).</summary>
    public IReadOnlyList<int> VolatilityWindows { get; init; } = [20, 60, 252];

    /// <summary>Beta, alpha and tracking-error windows (common days).</summary>
    public IReadOnlyList<int> BetaWindows { get; init; } = [60, 252];

    /// <summary>The window of the rolling beta series.</summary>
    public int RollingBetaWindow { get; init; } = 60;

    /// <summary>Relative strength return windows (sessions).</summary>
    public IReadOnlyList<int> RelativeStrengthWindows { get; init; } = [21, 63, 126, 252];

    /// <summary>Fewest index points for a drawdown.</summary>
    public int MinDrawdownPoints { get; init; } = 20;

    /// <summary>Fewest up days, and down days, for a capture ratio.</summary>
    public int MinCaptureDays { get; init; } = 10;

    /// <summary>Share of days that must have enough members for an index.</summary>
    public double MinIndexCoverage { get; init; } = 0.9;

    /// <summary>Most days a hold may start late or end early (P8-07).</summary>
    public int HoldMaxGapDays { get; init; } = 7;

    /// <summary>Days in the dispersion mean, and in the ADV.</summary>
    public int AveragingDays { get; init; } = 20;
}
