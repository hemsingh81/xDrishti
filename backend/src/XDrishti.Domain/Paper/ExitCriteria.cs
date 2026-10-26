namespace XDrishti.Domain.Paper;

/// <summary>The two paper exit statistics and the early warning (spec P10-04).</summary>
/// <param name="Expectancy">E3: positive expectancy after costs within the backtest band.</param>
/// <param name="Drawdown">E4: the largest fall within the 90th percentile of the same number of backtest trades.</param>
/// <param name="EarlyWarning">The warning sentence, or null. Not a verdict.</param>
public sealed record ExitCriteria(CriterionResult Expectancy, CriterionResult Drawdown, string? EarlyWarning);
