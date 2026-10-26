namespace XDrishti.Application.Reporting;

/// <summary>The bars of the replay window at one timeframe, adjusted for the corporate actions applied now, with the coherence of the stored entry against them.</summary>
/// <param name="Timeframe">1m, 5m, 15m, 1h or 1D.</param>
/// <param name="Adjusted">Always true: the bars are adjusted like the stored trade.</param>
/// <param name="AdjustedThrough">The ex-date of the last corporate action applied to the bars; null when none is.</param>
/// <param name="From">First instant of the window.</param>
/// <param name="To">Last instant of the window.</param>
/// <param name="BarCount">Bars returned (never above 5,000).</param>
/// <param name="Bars">The bars, oldest first.</param>
/// <param name="Coherence">Whether the bars agree with the stored entry.</param>
public sealed record ReplayChartView(string Timeframe, bool Adjusted, DateOnly? AdjustedThrough, DateTimeOffset From, DateTimeOffset To, int BarCount, IReadOnlyList<ReplayBarView> Bars, ReplayCoherenceView Coherence);
