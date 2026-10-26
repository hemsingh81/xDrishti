namespace XDrishti.Domain.Paper;

/// <summary>The cumulative R after <paramref name="Index"/> trades: the 5th, 50th and 95th percentile of the backtest draws and the paper line (spec P10-04, criterion 10).</summary>
/// <param name="Index">The number of trades, 1 or more.</param>
/// <param name="P05">5th percentile of the draws.</param>
/// <param name="P50">Median of the draws.</param>
/// <param name="P95">95th percentile of the draws.</param>
/// <param name="Paper">The paper trades' cumulative R after the same number of trades.</param>
public sealed record FanPoint(int Index, double P05, double P50, double P95, double Paper);
