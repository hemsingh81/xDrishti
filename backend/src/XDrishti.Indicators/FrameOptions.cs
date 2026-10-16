using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators;

/// <summary>
/// How a <see cref="FeatureFrame"/> is built (ADR 0032, ADR 0033). <see cref="Calendar"/> lets a special session close its last buckets and its day at its own last minute (without it only the regular 15:30 close is
/// known), and lets a week or a month close at its last session instead of at the first bar of the next one. <see cref="HistoryDepth"/> is how many closed bars of its own timeframe each feature remembers, <c>1</c> to <see cref="MaxHistoryDepth"/>: <c>At(handle, lag)</c> reads lags <c>0</c> to
/// <c>HistoryDepth − 1</c>. The memory is features × depth doubles, allocated once.
/// <para>
/// <see cref="Context"/> is the daily <see cref="MarketContext"/> the context features (<c>index_trend</c>, <c>index_adx</c>, <c>index_vol_pct</c>, <c>vix</c>, <c>vix_pct</c>, <c>vix_chg</c>, <c>breadth</c>,
/// <c>regime_trend</c>, <c>regime_vol</c>, <c>regime</c>) read; the frame only reads it, so any number of frames may share one, and one writer may <see cref="MarketContext.Append"/> to it while they run (a day is read only
/// once it is complete, and the frames see it from the next date on). A context that nobody extends makes the frames keep the values of its last day for ever (see <see cref="MarketContext"/>). <see cref="ExpiryDays"/> are the India
/// dates on which <c>is_expiry_day</c> is 1: the library does not guess them (NSE has changed its expiry weekdays more than once), the caller passes them from the instrument master; the frame takes a copy when it is
/// created (a <see cref="System.Collections.Frozen.FrozenSet{T}"/> is used as given, so one can be shared by many frames). A frame asked for a context feature without <see cref="Context"/>, or for <c>is_expiry_day</c> without <see cref="ExpiryDays"/>, is refused at <see cref="FeatureFrame.Create(IReadOnlyList{FeatureSpec}, FrameOptions?)"/>
/// (<c>options.context</c>, <c>options.expiryDays</c>); an empty set is valid. Neither is needed for <c>day_of_week</c>.
/// </para>
/// </summary>
public sealed record FrameOptions(
    TradingCalendar? Calendar = null,
    int HistoryDepth = FrameOptions.DefaultHistoryDepth,
    MarketContext? Context = null,
    IReadOnlySet<DateOnly>? ExpiryDays = null)
{
    public const int DefaultHistoryDepth = 8;

    public const int MaxHistoryDepth = 256;
}
