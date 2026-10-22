namespace XDrishti.Domain.Learning;

/// <summary>
/// The statistics of one cell in one window as of one date (P6-01, ADR 0064). Counts are plain; the win rate, the averages, the expectancy and the profit factor use the recency weights; the maximum drawdown and the quantiles do not.
/// A window with no trade has <see cref="N"/> 0, the prior as its win rate and nulls elsewhere.
/// </summary>
/// <param name="Cell">Who the cell is.</param>
/// <param name="Window">The window.</param>
/// <param name="AsOf">The as-of date: only trades that exited on or before it were read.</param>
/// <param name="Costed">R is net of charges; false means gross.</param>
/// <param name="N">Trades in the window.</param>
/// <param name="Wins">Trades with R above 0 (a trade at exactly 0 is not a win).</param>
/// <param name="EffectiveN">The sum of the recency weights.</param>
/// <param name="WinRateRaw">Wins over trades, unweighted; null for no trade.</param>
/// <param name="PriorRate">The parent group's win rate the cell is shrunk toward.</param>
/// <param name="WinRatePosterior">The posterior mean of the Beta: (weighted wins + strength x prior rate) / (effective n + strength).</param>
/// <param name="WinRateLower">The lower bound of the credible interval of the posterior: the confidence.</param>
/// <param name="WinRateUpper">The upper bound.</param>
/// <param name="AvgWinR">Recency-weighted mean R of the winning trades; null without a win.</param>
/// <param name="AvgLossR">Recency-weighted mean R of the losing trades (negative); null without a loss.</param>
/// <param name="ExpectancyR">Recency-weighted mean R; null for no trade.</param>
/// <param name="ProfitFactor">Weighted sum of winning R over weighted sum of losing R in absolute value; null without a loss.</param>
/// <param name="MaxDrawdownR">The largest peak-to-trough fall of the running R in the order of the exits (0 or positive).</param>
/// <param name="MaeP50">Median MAE in R.</param>
/// <param name="MaeP90">90th percentile MAE in R.</param>
/// <param name="MfeP50">Median MFE in R.</param>
/// <param name="MfeP90">90th percentile MFE in R.</param>
/// <param name="Enough">N is at least the options' minimum trades; false reads "not enough evidence".</param>
public sealed record CellStatsRow(
    CellId Cell,
    CellWindow Window,
    DateOnly AsOf,
    bool Costed,
    int N,
    int Wins,
    double EffectiveN,
    double? WinRateRaw,
    double PriorRate,
    double WinRatePosterior,
    double WinRateLower,
    double WinRateUpper,
    double? AvgWinR,
    double? AvgLossR,
    double? ExpectancyR,
    double? ProfitFactor,
    double MaxDrawdownR,
    double? MaeP50,
    double? MaeP90,
    double? MfeP50,
    double? MfeP90,
    bool Enough);
