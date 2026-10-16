using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// A higher-timeframe bar whose window is over (ADR 0031). <see cref="Bar"/> carries the bucket start as its <c>EpochSeconds</c> and the open, high, low, close and volume of the 1-minute bars in it;
/// <see cref="Bars"/> counts those minutes and <see cref="Complete"/> says whether every minute of the window was there — the same values the stored aggregate (<c>market.bars_agg</c>) holds.
/// </summary>
public readonly record struct ClosedBar(long StartEpochSeconds, Bar Bar, int Bars, bool Complete, Timeframe Timeframe);
