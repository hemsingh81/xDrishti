namespace XDrishti.Domain.Selection;

/// <summary>The stable reason codes of the eligibility filters (spec P7-01). They are stored and shown as they are; never rename one.</summary>
public static class FilterCodes
{
    /// <summary>The cell is at least in the minimum state.</summary>
    public const string CellState = "cell_state";

    /// <summary>The statistics were known before the decision date.</summary>
    public const string StatsAsOf = "stats_as_of";

    /// <summary>The cell has enough trades.</summary>
    public const string Sample = "sample";

    /// <summary>The shrunk win rate is high enough.</summary>
    public const string WinRate = "win_rate";

    /// <summary>The conservative expectancy in R is high enough.</summary>
    public const string Expectancy = "expectancy";

    /// <summary>The reward to risk before the obstacle is high enough.</summary>
    public const string RewardRisk = "reward_risk";

    /// <summary>The cost in R is low enough.</summary>
    public const string Cost = "cost";

    /// <summary>The average daily turnover is high enough.</summary>
    public const string Liquidity = "liquidity";

    /// <summary>No event excludes the instrument.</summary>
    public const string Event = "event";

    /// <summary>The instrument is not on the F&amp;O ban list.</summary>
    public const string FnoBan = "fno_ban";

    /// <summary>The instrument is not under surveillance.</summary>
    public const string Surveillance = "surveillance";

    /// <summary>Every code, in the order the filters run.</summary>
    public static IReadOnlyList<string> All { get; } =
        [CellState, StatsAsOf, Sample, WinRate, Expectancy, RewardRisk, Cost, Liquidity, Event, FnoBan, Surveillance];
}
