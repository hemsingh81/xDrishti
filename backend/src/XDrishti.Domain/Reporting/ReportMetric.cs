namespace XDrishti.Domain.Reporting;

/// <summary>The figures of a group that a pivot or a ranking can show.</summary>
public enum ReportMetric
{
    /// <summary>The number of trades; a count is always a fact.</summary>
    Trades,

    /// <summary>Wins over trades, unshrunk.</summary>
    WinRate,

    /// <summary>Mean R of the winning trades.</summary>
    AvgWinR,

    /// <summary>Mean R of the losing trades (negative).</summary>
    AvgLossR,

    /// <summary>Mean R per trade.</summary>
    ExpectancyR,

    /// <summary>Sum of winning R over the absolute sum of losing R.</summary>
    ProfitFactor,

    /// <summary>Sum of R.</summary>
    TotalR,

    /// <summary>Largest peak-to-trough fall of the running R in exit order.</summary>
    MaxDrawdownR,

    /// <summary>The most losing trades in a row in exit order.</summary>
    LongestLossStreak,

    /// <summary>Mean sessions held.</summary>
    AvgHoldingSessions,

    /// <summary>The win rate shrunk toward the parent group (Beta posterior mean, P6-01).</summary>
    ShrunkWinRate,

    /// <summary>The lower end of the credible interval of the shrunk win rate (P6-01).</summary>
    WinRateLower,

    /// <summary>Expectancy from the shrunk win rate and the mean win and loss.</summary>
    ShrunkExpectancyR,

    /// <summary>Expectancy from the lower credible win rate and the mean win and loss: the figure a ranking uses.</summary>
    ExpectancyLowerR,
}
