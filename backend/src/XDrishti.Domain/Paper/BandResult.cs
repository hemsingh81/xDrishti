namespace XDrishti.Domain.Paper;

/// <summary>What the composition-matched bootstrap says about the counted paper trades (spec P10-04). Plain numbers; the caller decides whether the sample size lets them be shown.</summary>
/// <param name="N">Paper trades (the size of every draw).</param>
/// <param name="Draws">Resamples taken.</param>
/// <param name="Seed">The seed of the band's generator.</param>
/// <param name="PaperMean">The paper mean R.</param>
/// <param name="IntervalLow">5th percentile of the paper mean's own bootstrap (the 90% interval's lower end).</param>
/// <param name="IntervalHigh">95th percentile of the same.</param>
/// <param name="BandP05">5th percentile of the draw means.</param>
/// <param name="BandP50">Median of the draw means.</param>
/// <param name="BandP95">95th percentile of the draw means.</param>
/// <param name="Rank">Percentile rank (0 to 100, ties count half) of the paper mean among the draw means.</param>
/// <param name="Position"><c>below</c> (mean under the 5th percentile), <c>within</c> or <c>above</c> (over the 95th).</param>
/// <param name="PaperMaxDrawdown">The paper trades' largest peak-to-trough fall of cumulative R, in exit order.</param>
/// <param name="DrawdownP50">Median of the draws' largest falls.</param>
/// <param name="DrawdownP90">90th percentile of the same: the reference of the exit criterion.</param>
/// <param name="DrawdownP95">95th percentile of the same.</param>
/// <param name="DrawdownPosition"><c>within</c> when the paper fall is at most the 90th percentile, else <c>above</c>.</param>
/// <param name="Fan">Cumulative R percentiles with the paper line (every trade up to <see cref="BacktestBand.MaxFanPoints"/> points).</param>
public sealed record BandResult(
    int N,
    int Draws,
    ulong Seed,
    double PaperMean,
    double IntervalLow,
    double IntervalHigh,
    double BandP05,
    double BandP50,
    double BandP95,
    double Rank,
    string Position,
    double PaperMaxDrawdown,
    double DrawdownP50,
    double DrawdownP90,
    double DrawdownP95,
    string DrawdownPosition,
    IReadOnlyList<FanPoint> Fan);
