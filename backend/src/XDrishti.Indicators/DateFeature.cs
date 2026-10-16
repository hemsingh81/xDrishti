using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// One date-driven feature (P3-01c, ADR 0033): a number that changes at most once per India date — the weekday, whether the date is an expiry day, or a value of the daily <see cref="MarketContext"/> — and is read
/// as an indicator so that the frame can treat it like the others. It is fed a closed 1-minute bar at a time, but only the <b>first bar of each India date</b> matters: a bar of the date it already holds changes nothing, so
/// the value stays the same for the whole date (09:15 and 15:29 read the same number). <see cref="FeatureFrame"/> feeds its date lane only at the first accepted step of each date, which is also when it writes the
/// feature's history (one entry per date).
/// <para>
/// <b>Strictly before today.</b> A context value at the date <i>D</i> is that of the latest context day strictly before <i>D</i> (<see cref="MarketContext.TryAsOf(DateOnly, out ContextValues)"/>): a strategy at 10:30
/// on a Tuesday sees Monday's close, never Tuesday's. <see cref="DateFeatureKind.Weekday"/> and <see cref="DateFeatureKind.ExpiryDay"/> read the date itself. A date outside the range of <see cref="DateOnly"/>
/// (a garbage epoch) has no context and no expiry day: those values are not ready, and the weekday is still computed from the day number.
/// </para>
/// <para>
/// <b>Not ready.</b> A feature that reads the context (every kind but <see cref="DateFeatureKind.Weekday"/> and <see cref="DateFeatureKind.ExpiryDay"/>) is <see cref="double.NaN"/> without a <see cref="MarketContext"/>, before the context has a day earlier than the date, and while the value the day carries is not ready (the warm-up
/// of its rolling windows, a day without a VIX, a day without a breadth). <see cref="DateFeatureKind.Weekday"/> is ready from the first bar; <see cref="DateFeatureKind.ExpiryDay"/> from the first bar once it
/// has been given a set of dates (an empty set is a valid set: always 0).
/// </para>
/// <para>
/// <b>Past the last day of the context.</b> A date after the last day the context holds reads that last day, however far away it is, and the feature stays ready: the context does not know the clock and the frame does not
/// expose the date the value belongs to (<see cref="ContextValues.Date"/> is dropped here). A run whose context is not extended (<see cref="MarketContext.Append"/>) therefore keeps trading on a stale
/// <c>regime</c> or <c>vix_pct</c> without a signal. The caller must extend the context as the days pass and check its age (<see cref="MarketContext.TryAsOf(DateOnly, out ContextValues)"/> returns the day); ADR 0033 lists this as a
/// known risk for the context data job and the market gate.
/// </para>
/// <para>
/// <b>Warm-up</b> is counted in <b>days of the context</b>, not in bars: the number of context days that must lie before the date for the feature to be ready on a context with no gaps (and, where it matters, a VIX
/// on each day) — for example 55 for <c>index_trend</c> and 265 for <c>regime</c> with the default <see cref="ContextOptions"/>. It is exact for the options of the context the instance was given, and for the
/// default options when it has none; <see cref="DateFeatureKind.Weekday"/> and <see cref="DateFeatureKind.ExpiryDay"/> report 1 (ready at the first date).
/// </para>
/// <para>Thread-confined like every indicator; the <see cref="MarketContext"/> behind it may be shared by any number of instances and frames.</para>
/// </summary>
public sealed class DateFeature : IIndicator
{
    private static readonly ContextOptions DefaultOptions = new();

    private readonly DateFeatureKind _kind;

    private readonly MarketContext? _context;

    private readonly IReadOnlySet<DateOnly>? _expiryDays;

    private readonly int _warmUp;

    private int _cursor;

    private long _day;

    private bool _started;

    private double _value = double.NaN;

    /// <param name="kind">Which feature this instance reports.</param>
    /// <param name="context">The daily context the features that read it use; none gives a feature that is never ready (the weekday and the expiry day do not need it).</param>
    /// <param name="expiryDays">The India dates that are expiry days, for <see cref="DateFeatureKind.ExpiryDay"/>; none gives a feature that is never ready. The set is read as given, so it must not change while the
    /// feature is in use (<see cref="FeatureFrame"/> takes its own copy).</param>
    public DateFeature(DateFeatureKind kind, MarketContext? context = null, IReadOnlySet<DateOnly>? expiryDays = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a known date feature.");
        }

        _kind = kind;
        _context = context;
        _expiryDays = expiryDays;
        _warmUp = WarmUpDays(kind, context?.Options ?? DefaultOptions);
    }

    /// <summary>Which feature this instance reports.</summary>
    public DateFeatureKind Kind => _kind;

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => _warmUp;

    /// <summary>Takes the value for the India date of the bar when it is the first bar of a new date; a bar of the date already held changes nothing.</summary>
    public void Update(in Bar bar)
    {
        var day = IndiaDays.DayOf(bar.EpochSeconds);
        if (_started && day == _day)
        {
            return;
        }

        _started = true;
        _day = day;
        _value = Read(day);
    }

    public void Reset()
    {
        _cursor = 0;
        _day = 0;
        _started = false;
        _value = double.NaN;
    }

    /// <summary>Whether the feature reads the <see cref="MarketContext"/> (everything but the two calendar features): the catalog sets <see cref="IndicatorInfo.RequiresContext"/> from it.</summary>
    internal static bool ReadsContext(DateFeatureKind kind) => kind is not (DateFeatureKind.Weekday or DateFeatureKind.ExpiryDay);

    /// <summary>
    /// The context days that must precede the date for the feature to be ready, for a context with no gaps: the average, its slope and the ADX for the trend (the later of the two), the ATR and a full window of its
    /// values for the volatility percentile, a full window of VIX closes for the VIX percentile, a second VIX for the change, and one day for the rest. The regime needs both parts.
    /// </summary>
    internal static int WarmUpDays(DateFeatureKind kind, ContextOptions options)
    {
        var trend = Math.Max(options.TrendSmaPeriod + options.TrendSlopeDays, 2 * options.AdxPeriod);
        var volatility = options.AtrPeriod + options.PercentileWindow - 1;
        return kind switch
        {
            DateFeatureKind.Weekday or DateFeatureKind.ExpiryDay or DateFeatureKind.Vix or DateFeatureKind.Breadth => 1,
            DateFeatureKind.VixChg => 2,
            DateFeatureKind.VixPct => options.PercentileWindow,
            DateFeatureKind.IndexAdx => 2 * options.AdxPeriod,
            DateFeatureKind.IndexTrend or DateFeatureKind.RegimeTrend => trend,
            DateFeatureKind.IndexVolPct or DateFeatureKind.RegimeVol => volatility,
            DateFeatureKind.Regime => Math.Max(trend, volatility),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a known date feature."),
        };
    }

    /// <summary>The ISO weekday (Monday 1 … Sunday 7) of an India day number; 1970-01-01, day 0, was a Thursday.</summary>
    private static double IsoWeekday(long day) => IndiaDays.DaysSinceMonday(day) + 1;

    private double Read(long day)
    {
        if (_kind == DateFeatureKind.Weekday)
        {
            return IsoWeekday(day);
        }

        if (!IndiaDays.TryDateOf(day, out var date))
        {
            return double.NaN;
        }

        if (_kind == DateFeatureKind.ExpiryDay)
        {
            return _expiryDays is null ? double.NaN : _expiryDays.Contains(date) ? 1.0 : 0.0;
        }

        if (_context is null || !_context.TryAsOf(date, ref _cursor, out var values))
        {
            return double.NaN;
        }

        return _kind switch
        {
            DateFeatureKind.IndexTrend => values.IndexTrend,
            DateFeatureKind.IndexAdx => values.IndexAdx,
            DateFeatureKind.IndexVolPct => values.IndexVolPct,
            DateFeatureKind.Vix => values.Vix,
            DateFeatureKind.VixPct => values.VixPct,
            DateFeatureKind.VixChg => values.VixChg,
            DateFeatureKind.Breadth => values.Breadth,
            DateFeatureKind.RegimeTrend => values.RegimeTrend,
            DateFeatureKind.RegimeVol => values.RegimeVol,
            _ => values.Regime,
        };
    }
}
