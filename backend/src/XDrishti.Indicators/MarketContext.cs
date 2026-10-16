using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// The daily market context the strategies condition on (P3-01c, ADR 0033): the regime index's trend, ADX and volatility percentile, the VIX, its percentile and change, the breadth, and the regime built from
/// them — one <see cref="ContextValues"/> per <see cref="ContextDay"/>. <b>Every value of a day is computed from that day and the days before it only</b> (the streaming <see cref="Sma"/>, <see cref="Atr"/> and
/// <see cref="Adx"/> of this library are fed the daily index bars, and a <c>PercentileWindow</c> ranks the volatility), so the values of the first <i>k</i> days are identical whatever comes after them. A reader at
/// <i>D</i> gets <see cref="TryAsOf(DateOnly, out ContextValues)"/>: the values of the <b>latest day strictly before <i>D</i></b> — a frame at 10:30 on a Tuesday sees Monday's close, never Tuesday's, even though
/// Tuesday's record is in the context.
/// <para>
/// <b>Gaps.</b> Dates must increase but may skip (weekends, holidays): a gap is no day, not a zero, and a date inside a gap reads the day before it. A day without a VIX makes only the VIX values
/// (<c>vix</c>, <c>vix_chg</c>, <c>vix_pct</c>) not ready on that day — nothing is carried forward — and the volatility score falls back to the index percentile alone; <c>vix_chg</c> compares with the
/// previous day <i>of the context</i> and is not ready when that day has no VIX. The window of <c>vix_pct</c> holds the last <see cref="ContextOptions.PercentileWindow"/> days <i>that have</i> a VIX (the days
/// without one are not in it), so a gap does not erase a year of history; the window of <c>index_vol_pct</c> holds the last that many ATR values.
/// </para>
/// <para>
/// <b>Threading.</b> Built once and then shared by any number of frames: <see cref="TryAsOf(DateOnly, out ContextValues)"/> writes nothing (no cache). <see cref="Append"/> is <b>single-writer</b> — one thread at a
/// time, the type takes no lock — but readers may run while it appends: a day is written completely before it is counted and the storage grows by copy, so a reader sees the days that were complete when it started and
/// never a half-written one (a live run appends today's close at 15:35 while frames exist). Invalid input is never thrown: <see cref="Create"/> and <see cref="Append"/> return a validation error and leave the context unchanged.
/// </para>
/// <para>
/// <b>Past the last day.</b> A reader at a date after the last day of the context gets that last day, however far away the date is: the context does not know what the clock says and the values carry no age
/// (<see cref="ContextValues.Date"/> says which day they belong to, but <see cref="ContextFeature"/> and so the frame do not pass it on). <b>The caller must extend the context</b> (<see cref="Append"/>) as the days pass; a context
/// that is not extended makes the features keep the last day's values and stay ready (ADR 0033 lists it as a known risk for the context data job and the market gate).
/// </para>
/// </summary>
public sealed class MarketContext
{
    /// <summary>The options violate a limit (the error names each field, <c>options.percentileWindow</c>).</summary>
    public const string InvalidOptionsCode = "indicators.context_options_invalid";

    /// <summary>A day (or the whole sequence) is null.</summary>
    public const string MissingDayCode = "indicators.context_day_missing";

    /// <summary>The date is not after the date of the last day of the context.</summary>
    public const string DateNotIncreasingCode = "indicators.context_date_not_increasing";

    /// <summary>An index price is not a positive, finite number.</summary>
    public const string PriceNotPositiveCode = "indicators.context_price_not_positive";

    /// <summary>The index high is below the index low.</summary>
    public const string HighBelowLowCode = "indicators.context_high_below_low";

    /// <summary>The index close is above the high or below the low.</summary>
    public const string CloseOutsideRangeCode = "indicators.context_close_outside_range";

    /// <summary>The VIX close is not a positive, finite number.</summary>
    public const string VixNotPositiveCode = "indicators.context_vix_not_positive";

    /// <summary>The breadth is not a number from 0 to 100.</summary>
    public const string BreadthOutOfRangeCode = "indicators.context_breadth_out_of_range";

    /// <summary>How many days the cursor read walks forward before it searches (consecutive dates of a frame move 0 or 1).</summary>
    private const int MaxForwardSteps = 4;

    private readonly Sma _sma;

    private readonly Atr _atr;

    private readonly Adx _adx;

    private readonly PercentileWindow _volatilityWindow;

    private readonly PercentileWindow _vixWindow;

    private readonly double[] _smaHistory;

    private int _smaNext;

    private int _smaCount;

    private double _previousVix = double.NaN;

    // Published by the single writer: the element is written, then the count is raised with a release; the array is replaced (copy-on-write) before the count that needs the room. Readers take the count first.
    private ContextValues[] _values = [];

    private int _count;

    private MarketContext(ContextOptions options)
    {
        Options = options;
        _sma = new Sma(options.TrendSmaPeriod);
        _atr = new Atr(options.AtrPeriod);
        _adx = new Adx(options.AdxPeriod);
        _volatilityWindow = new PercentileWindow(options.PercentileWindow);
        _vixWindow = new PercentileWindow(options.PercentileWindow);
        _smaHistory = new double[options.TrendSlopeDays];
    }

    /// <summary>The numbers this context was built with.</summary>
    public ContextOptions Options { get; }

    /// <summary>The number of days in the context.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// Builds the context from daily records in strictly increasing date order. The options and each record are validated before anything is computed from them: the first problem is returned (the error's field
    /// names the record, <c>days[17].indexClose</c>) and nothing is thrown. An empty sequence gives an empty context.
    /// </summary>
    /// <param name="days">The records, oldest first.</param>
    /// <param name="options">The numbers of the trend and volatility rules; the defaults when null.</param>
    public static Result<MarketContext> Create(IEnumerable<ContextDay> days, ContextOptions? options = null)
    {
        options ??= new ContextOptions();
        if (options.Problem() is { } invalid)
        {
            return Result.Failure<MarketContext>(invalid);
        }

        if (days is null)
        {
            return Result.Failure<MarketContext>(Invalid(MissingDayCode, "days", "The days are missing."));
        }

        var context = new MarketContext(options);
        if (days.TryGetNonEnumeratedCount(out var expected))
        {
            context.EnsureCapacity(expected);
        }

        var index = 0;
        foreach (var day in days)
        {
            if (context.Check(day, index) is { } problem)
            {
                return Result.Failure<MarketContext>(problem);
            }

            context.Store(day);
            index++;
        }

        return Result.Success(context);
    }

    /// <summary>
    /// Adds the next day. Its date must be after the last one and the record must be possible (see <see cref="ContextDay"/>); otherwise the validation error is returned (the field is <c>day.indexHigh</c> and
    /// so on) and the context stays as it was. Single-writer: one thread at a time; readers may run meanwhile and see the day only once it is complete.
    /// </summary>
    public Result Append(ContextDay day)
    {
        if (Check(day, -1) is { } problem)
        {
            return Result.Failure(problem);
        }

        Store(day);
        return Result.Success();
    }

    /// <summary>
    /// The values of the latest day strictly before <paramref name="date"/>: false when there is none (the context is empty, or <paramref name="date"/> is on or before its first day). A record dated
    /// <paramref name="date"/> is in the context but is not read: a frame at any minute of that date sees the day before. Binary search, O(log n); writes nothing, so any number of frames may call it.
    /// </summary>
    public bool TryAsOf(DateOnly date, out ContextValues values)
    {
        var days = Days(out var count);
        var datesRead = 0;
        var index = LastBefore(days, 0, count, date, ref datesRead);
        if (index < 0)
        {
            values = default;
            return false;
        }

        values = days[index];
        return true;
    }

    /// <summary>
    /// The same read for a caller that walks forward through dates (a frame, once per India date): <paramref name="cursorHint"/> is the caller's own position — the index of its previous answer — so a date that
    /// is a day or a few days later costs O(1), and any other date (a jump forward or back, a fresh frame with 0) falls back to the binary search. Whatever the hint, the answer is that of
    /// <see cref="TryAsOf(DateOnly, out ContextValues)"/>; on a hit the hint is moved to the answer, on a miss it is left alone. Nothing is written to the context.
    /// </summary>
    internal bool TryAsOf(DateOnly date, ref int cursorHint, out ContextValues values) => TryAsOf(date, ref cursorHint, out values, out _);

    /// <summary>
    /// <see cref="TryAsOf(DateOnly, ref int, out ContextValues)"/> that also says how many dates of the context the search looked at (<paramref name="datesRead"/>): a test proves from it that the walk is O(1) — at most
    /// <c>MaxForwardSteps + 1</c> for a date up to that many days ahead of the hint (3 for dates a calendar day apart), whatever the length of the context — and not a binary search in disguise.
    /// </summary>
    internal bool TryAsOf(DateOnly date, ref int cursorHint, out ContextValues values, out int datesRead)
    {
        var days = Days(out var count);
        var index = Locate(days, count, date, cursorHint, out datesRead);
        if (index < 0)
        {
            values = default;
            return false;
        }

        cursorHint = index;
        values = days[index];
        return true;
    }

    private static Error Invalid(string code, string path, string message) =>
        Error.Validation(code, message) with { Fields = [new FieldError(path, message)] };

    private static bool IsPositive(double price) => double.IsFinite(price) && price > 0;

    private static double Percent(double previous, double current) => (current - previous) / previous * 100.0;

    private static string FieldPath(int index, string field) =>
        (index < 0 ? "day." : string.Create(CultureInfo.InvariantCulture, $"days[{index}].")) + field;

    /// <summary>The first reason the record cannot be the next day of the context, or null. Reads the state only; nothing is changed.</summary>
    private Error? Check(ContextDay? day, int index)
    {
        if (day is null)
        {
            return Invalid(MissingDayCode, index < 0 ? "day" : string.Create(CultureInfo.InvariantCulture, $"days[{index}]"), "The day is missing.");
        }

        if (_count > 0 && day.Date <= _values[_count - 1].Date)
        {
            return Invalid(
                DateNotIncreasingCode,
                FieldPath(index, "date"),
                string.Create(CultureInfo.InvariantCulture, $"The date {day.Date:yyyy-MM-dd} must be after the last date {_values[_count - 1].Date:yyyy-MM-dd}."));
        }

        if (!IsPositive(day.IndexHigh))
        {
            return Invalid(PriceNotPositiveCode, FieldPath(index, "indexHigh"), "The index high must be a positive number.");
        }

        if (!IsPositive(day.IndexLow))
        {
            return Invalid(PriceNotPositiveCode, FieldPath(index, "indexLow"), "The index low must be a positive number.");
        }

        if (!IsPositive(day.IndexClose))
        {
            return Invalid(PriceNotPositiveCode, FieldPath(index, "indexClose"), "The index close must be a positive number.");
        }

        if (day.IndexHigh < day.IndexLow)
        {
            return Invalid(HighBelowLowCode, FieldPath(index, "indexHigh"), "The index high must not be below the index low.");
        }

        if (day.IndexClose > day.IndexHigh || day.IndexClose < day.IndexLow)
        {
            return Invalid(CloseOutsideRangeCode, FieldPath(index, "indexClose"), "The index close must be between the index low and the index high.");
        }

        if (day.VixClose is { } vix && !IsPositive(vix))
        {
            return Invalid(VixNotPositiveCode, FieldPath(index, "vixClose"), "The VIX close must be a positive number when it is given.");
        }

        if (day.Breadth is { } breadth && breadth is not (>= 0 and <= 100))
        {
            return Invalid(BreadthOutOfRangeCode, FieldPath(index, "breadth"), "The breadth must be a percent from 0 to 100 when it is given.");
        }

        return null;
    }

    /// <summary>Computes the values of a record already checked and appends them. The indicators see the day before anything is derived from it, and nothing after it.</summary>
    private void Store(ContextDay day)
    {
        var bar = new Bar(IndiaDays.MidnightOf(IndiaDays.DayNumberOf(day.Date)), day.IndexClose, day.IndexHigh, day.IndexLow, day.IndexClose, 0);
        _sma.Update(bar);
        _atr.Update(bar);
        _adx.Update(bar);

        var window = Options.PercentileWindow;
        var trend = Trend(day.IndexClose);
        var indexRank = _atr.IsReady ? _volatilityWindow.PushDoubleRank(_atr.Value / day.IndexClose * 100.0) : PercentileWindow.NotRanked;
        var indexVolPct = PercentileWindow.Percent(indexRank, window);

        var vix = double.NaN;
        var vixRank = PercentileWindow.NotRanked;
        var vixChg = double.NaN;
        if (day.VixClose is { } close)
        {
            vix = close;
            vixRank = _vixWindow.PushDoubleRank(close);
            vixChg = double.IsNaN(_previousVix) ? double.NaN : Percent(_previousVix, close);
        }

        _previousVix = vix;

        var regimeVol = RegimeVol(indexRank, vixRank, window, Options);
        var regime = double.IsNaN(trend) || double.IsNaN(regimeVol) ? double.NaN : (1.0 - trend) * 3.0 + regimeVol + 1.0;

        Publish(new ContextValues(day.Date, trend, _adx.Value, indexVolPct, vix, PercentileWindow.Percent(vixRank, window), vixChg, day.Breadth ?? double.NaN, trend, regimeVol, regime));
    }

    /// <summary>Adds the finished day: the element is written first, then the count is raised (a release), so a reader that sees the new count sees the whole element; the storage grows by copy before the count needs the room.</summary>
    private void Publish(in ContextValues values)
    {
        var count = _count;
        if (count == _values.Length)
        {
            EnsureCapacity(Math.Max(16, count * 2));
        }

        _values[count] = values;
        Volatile.Write(ref _count, count + 1);
    }

    /// <summary>
    /// +1 / −1 / 0 for the day just fed, NaN until the average, its slope and the ADX all exist. The slope compares the average of the day with the average <c>TrendSlopeDays</c> days earlier, kept in a small ring
    /// that is advanced on every day the average exists, whatever the ADX says.
    /// </summary>
    private double Trend(double close)
    {
        if (!_sma.IsReady)
        {
            return double.NaN;
        }

        var average = _sma.Value;
        var slope = double.NaN;
        if (_smaCount == _smaHistory.Length)
        {
            slope = average / _smaHistory[_smaNext] - 1.0;
        }
        else
        {
            _smaCount++;
        }

        _smaHistory[_smaNext] = average;
        _smaNext = _smaNext + 1 == _smaHistory.Length ? 0 : _smaNext + 1;

        var adx = _adx.Value;
        if (double.IsNaN(slope) || double.IsNaN(adx))
        {
            return double.NaN;
        }

        if (adx >= Options.AdxMin)
        {
            if (close > average && slope > 0)
            {
                return 1.0;
            }

            if (close < average && slope < 0)
            {
                return -1.0;
            }
        }

        return 0.0;
    }

    /// <summary>
    /// 0 (low), 1 (normal) or 2 (high) from the volatility score — the mean of the VIX percentile and the index percentile, or the index percentile alone when <paramref name="vixRank"/> is
    /// <see cref="PercentileWindow.NotRanked"/> (the day has no VIX or fewer than a window of them) — and NaN without the index percentile. The ranks are <b>twice the rank</b> (<c>2 × #below + #equal</c>) of the newest value among
    /// <paramref name="window"/> values. The score is <c>100 × rank / (2 × window)</c>, or <c>100 × (indexRank + vixRank) / (4 × window)</c> for the mean, computed as <b>one division of exact integers</b>: the result is
    /// the double nearest to the true fraction, which is also what <c>100.0 / 3</c> and <c>200.0 / 3</c> are, so a score that is exactly one third (two thirds) of 100 is never below (above) the default limits because of
    /// rounding. The limits keep their meaning: strictly <b>below</b> <see cref="ContextOptions.LowVolBelow"/> is low, <b>from</b> <see cref="ContextOptions.HighVolFrom"/> is high.
    /// </summary>
    internal static double RegimeVol(int indexRank, int vixRank, int window, ContextOptions options)
    {
        if (indexRank < 0)
        {
            return double.NaN;
        }

        var score = vixRank < 0 ? 100.0 * indexRank / (2.0 * window) : 100.0 * (indexRank + vixRank) / (4.0 * window);
        return score < options.LowVolBelow ? 0.0 : score >= options.HighVolFrom ? 2.0 : 1.0;
    }

    private void EnsureCapacity(int capacity)
    {
        if (capacity > _values.Length)
        {
            // Copy-on-write: a reader that holds the old array still sees every day it was told about.
            var bigger = new ContextValues[capacity];
            Array.Copy(_values, bigger, _count);
            Volatile.Write(ref _values, bigger);
        }
    }

    /// <summary>The days and how many of them are complete, for one read: the count is read first (an acquire), so the array read after it holds at least that many finished days.</summary>
    private ContextValues[] Days(out int count)
    {
        count = Volatile.Read(ref _count);
        return Volatile.Read(ref _values);
    }

    /// <summary>The index of the latest day strictly before the date, walking from the caller's hint when that is close, or -1. <paramref name="datesRead"/> counts the dates of the days that were looked at.</summary>
    private static int Locate(ContextValues[] days, int count, DateOnly date, int hint, out int datesRead)
    {
        datesRead = 0;
        if (count == 0)
        {
            return -1;
        }

        var at = Math.Clamp(hint, 0, count - 1);
        datesRead++;
        if (days[at].Date >= date)
        {
            return LastBefore(days, 0, at, date, ref datesRead);
        }

        for (var step = 0; step < MaxForwardSteps; step++)
        {
            if (at + 1 == count)
            {
                return at;
            }

            datesRead++;
            if (days[at + 1].Date >= date)
            {
                return at;
            }

            at++;
        }

        return LastBefore(days, at, count, date, ref datesRead);
    }

    /// <summary>The index of the last day before the date within <c>[low, high)</c> (the days are in date order), or <c>low − 1</c> when there is none; every date looked at is added to <paramref name="datesRead"/>.</summary>
    private static int LastBefore(ContextValues[] days, int low, int high, DateOnly date, ref int datesRead)
    {
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            datesRead++;
            if (days[middle].Date < date)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low - 1;
    }
}
