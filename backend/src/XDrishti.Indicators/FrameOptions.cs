using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators;

/// <summary>
/// How a <see cref="FeatureFrame"/> is built (ADR 0032, ADR 0033). <see cref="Calendar"/> lets a special session close its last buckets and its day at its own last minute (without it only the regular 15:30 close is
/// known), and lets a week or a month close at its last session instead of at the first bar of the next one. <see cref="HistoryDepth"/> is how many closed bars of its own timeframe each feature remembers, <c>1</c> to <see cref="MaxHistoryDepth"/>: <c>At(handle, lag)</c> reads lags <c>0</c> to
/// <c>HistoryDepth − 1</c>. The memory is features × depth doubles, allocated once.
/// <para>
/// <see cref="ExpiryDays"/> are the India
/// dates on which <c>is_expiry_day</c> is 1: the library does not guess them (NSE has changed its expiry weekdays more than once), the caller passes them from the instrument master; the frame takes a copy when it is
/// created (a <see cref="System.Collections.Frozen.FrozenSet{T}"/> is used as given, so one can be shared by many frames). A frame asked for <c>is_expiry_day</c> without <see cref="ExpiryDays"/>, is refused at <see cref="FeatureFrame.Create(IReadOnlyList{FeatureSpec}, FrameOptions?)"/>
/// (<c>options.expiryDays</c>); an empty set is valid. It is not needed for <c>day_of_week</c>. <see cref="ResultsDays"/> (P15-05) is the per-date series of <c>days_to_results</c> and <c>days_since_results</c> of one instrument, built by the caller as of the start of each date; a date it does not hold reads NaN,
/// and a frame asked for either feature without it is refused on <c>options.resultsDays</c>. Neutral numbers only: this library knows nothing of filings.
/// <see cref="RelativeStrength"/> (BK-09) holds the stored relative-strength rows of one instrument that <c>mom_rank</c>, <c>rs_vs_index</c> and <c>sector_rs</c> read as of the previous closed session; a frame asked for one without it is refused on <c>options.relativeStrength</c> (an empty set is valid: every read is NaN).
/// </para>
/// </summary>
public sealed record FrameOptions(
    TradingCalendar? Calendar = null,
    int HistoryDepth = FrameOptions.DefaultHistoryDepth,
    IReadOnlySet<DateOnly>? ExpiryDays = null,
    IReadOnlyDictionary<DateOnly, ResultsDayValues>? ResultsDays = null,
    RelativeStrengthDays? RelativeStrength = null)
{
    public const int DefaultHistoryDepth = 8;

    public const int MaxHistoryDepth = 256;
}
