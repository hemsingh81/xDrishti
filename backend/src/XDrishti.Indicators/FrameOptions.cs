using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators;

/// <summary>
/// How a <see cref="FeatureFrame"/> is built (ADR 0032). <see cref="Calendar"/> lets a special session close its last buckets and its day at its own last minute (without it only the regular 15:30 close is
/// known). <see cref="HistoryDepth"/> is how many closed bars of its own timeframe each feature remembers, <c>1</c> to <see cref="MaxHistoryDepth"/>: <c>At(handle, lag)</c> reads lags <c>0</c> to
/// <c>HistoryDepth − 1</c>. The memory is features × depth doubles, allocated once.
/// </summary>
public sealed record FrameOptions(TradingCalendar? Calendar = null, int HistoryDepth = FrameOptions.DefaultHistoryDepth)
{
    public const int DefaultHistoryDepth = 8;

    public const int MaxHistoryDepth = 256;
}
