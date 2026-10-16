using System.Collections.Frozen;
using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators;

/// <summary>
/// The DSL's view of the market at one 1-minute step (ADR 0031, ADR 0032): for every requested <see cref="FeatureSpec"/> the value as of the <b>last closed bar of its timeframe</b> — never the bar still forming. The
/// frame is given the specs once, builds only the <see cref="TimeframeBuilder"/>s and indicators they need (two specs for the same feature share one instance) and then advances with <see cref="Step"/>, one closed
/// 1-minute bar at a time. Values are read by the numeric handle of a spec (its position in the list given to <see cref="Create(IReadOnlyList{FeatureSpec}, FrameOptions?)"/>), which is allocation-free.
/// <para>
/// <b>Closed flags.</b> <see cref="BarClosed"/>, <see cref="ClosedBarsAt"/> and <see cref="TryLastClosed"/> say what the last step closed on each timeframe the frame was asked for. <b>History.</b> Each feature
/// remembers the values it had after each of the last <see cref="HistoryDepth"/> closed bars <i>of its own timeframe</i>: <see cref="At"/> and <see cref="IsReadyAt"/> read them (<c>close[1]</c>, crossings,
/// <c>rising(x, n)</c>). The history is written only when the feature's timeframe closes a bar — twice in one step when the minute closes two buckets. <b>Ordering guard.</b> A 1-minute bar at or before the previous
/// one is counted in <see cref="IgnoredBars"/> and skipped by every lane, so the 1-minute lane and the builders cannot diverge.
/// </para>
/// <para>
/// Incomplete higher-timeframe bars (a minute was missing) are consumed like any other and counted in <see cref="IncompleteBarsConsumed"/>. Thread-confined: one frame per instrument and worker.
/// </para>
/// <para>
/// <b>1W and 1M.</b> A weekly or monthly feature reads the closed weeks or months only — never the one running —, so <c>close</c> on 1W is the close of the last week that has closed, at every minute of the week in
/// progress before its closing step. With a <see cref="TradingCalendar"/> in <see cref="FrameOptions"/> a week or month closes at the step that closes the <i>last session of the period</i> (the same step at which the 1D lane closes that
/// date); without one, at the first bar of the next period: later, never earlier (see <see cref="TimeframeBuilder"/>). Closed flags, <see cref="At"/> (in weekly or monthly bars) and the warm-up (in bars of the feature's
/// own timeframe: <c>ema(10)</c> on 1W needs ten weeks) work as for every other timeframe. The session-bound features (<c>vwap</c>, <c>or_high</c>, <c>or_low</c>, <c>prev_day_*</c>, <c>today_*</c>, <c>gap_pct</c>) are
/// refused on 1W and 1M (see <see cref="IndicatorInfo.Supports"/>).
/// </para>
/// <para>
/// <b>The calendar and the market context (ADR 0033).</b> The twelve date-driven features (<c>day_of_week</c>, <c>is_expiry_day</c> and the ten that read <see cref="FrameOptions.Context"/>: <c>index_trend</c>,
/// <c>index_adx</c>, <c>index_vol_pct</c>, <c>vix</c>, <c>vix_pct</c>, <c>vix_chg</c>, <c>breadth</c>, <c>regime_trend</c>, <c>regime_vol</c>, <c>regime</c>) are asked for on 1m and live in a <i>date lane</i> of their own.
/// At the <b>first accepted step of each India date</b> <i>D</i> the lane refreshes them all: the weekday and the expiry flag from <i>D</i> itself, the context features from the latest context day <b>strictly before</b>
/// <i>D</i> (a day record dated <i>D</i> is in the context but is never read on <i>D</i>, so the value at 09:15 and at 15:29 is the same and a strategy never sees today's close). Every other step of the date changes nothing.
/// The history of these features is written once per date, so <see cref="At"/>(h, 1) is the value the previous date of the frame had; a bar the ordering guard skips does not refresh the lane. The lane adds no
/// allocation, also at the step that changes the date, and a <see cref="FrameOptions.Context"/> shared by many frames is only read. A frame with such a feature has a 1m lane even if no other feature is on 1m, so
/// <see cref="BarClosed"/>(M1) works for it.
/// </para>
/// </summary>
public sealed class FeatureFrame
{
    public const string InvalidCode = "indicators.invalid_features";

    private readonly Lane[] _lanes;

    private readonly Lane? _dateLane;

    private readonly Lane?[] _laneByTimeframe;

    private readonly IIndicator[] _byHandle;

    private readonly Lane[] _laneOfHandle;

    private readonly int[] _historyBase;

    private readonly FeatureSpec[] _specs;

    private readonly Dictionary<FeatureSpec, int> _handles;

    private readonly int _depth;

    private bool _seenBar;

    private long _lastEpoch;

    private FeatureFrame(Lane[] lanes, Lane? dateLane, Lane?[] laneByTimeframe, IIndicator[] byHandle, Lane[] laneOfHandle, int[] historyBase, FeatureSpec[] specs, Dictionary<FeatureSpec, int> handles, int depth)
    {
        _lanes = lanes;
        _dateLane = dateLane;
        _laneByTimeframe = laneByTimeframe;
        _byHandle = byHandle;
        _laneOfHandle = laneOfHandle;
        _historyBase = historyBase;
        _specs = specs;
        _handles = handles;
        _depth = depth;
    }

    /// <summary>The number of handles (the specs given to <see cref="Create(IReadOnlyList{FeatureSpec}, FrameOptions?)"/>, duplicates included).</summary>
    public int Count => _byHandle.Length;

    /// <summary>How many closed bars of its own timeframe each feature remembers; <see cref="At"/> reads lags <c>0</c> to <c>HistoryDepth − 1</c>.</summary>
    public int HistoryDepth => _depth;

    /// <summary>How many distinct indicator instances run (specs for the same feature share one).</summary>
    internal int InstanceCount => _lanes.Sum(l => l.Indicators.Length) + (_dateLane?.Indicators.Length ?? 0);

    /// <summary>How many timeframe builders run (one per higher timeframe asked for).</summary>
    internal int BuilderCount => _lanes.Count(l => l.Builder is not null);

    /// <summary>The number of 1-minute bars accepted so far (a bar ignored by the ordering guard is not a step).</summary>
    public long Steps { get; private set; }

    /// <summary>The number of 1-minute bars skipped because they were at or before the previous one (duplicates and out-of-order bars; 0 on valid data).</summary>
    public long IgnoredBars { get; private set; }

    /// <summary>The number of incomplete higher-timeframe bars (a minute was missing) the indicators have consumed.</summary>
    public long IncompleteBarsConsumed { get; private set; }

    /// <summary>The value of the feature as of the last closed bar of its timeframe; <see cref="double.NaN"/> while it is not ready.</summary>
    public double this[int handle] => _byHandle[handle].Value;

    /// <summary>
    /// Validates the options and the specs before any bar is fed and builds the frame. An unknown indicator, a parameter outside its range or in a bad combination (<c>fast ≥ slow</c>), a timeframe other than
    /// 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M, an indicator that cannot run on the timeframe (<see cref="IndicatorInfo.Supports"/>: its message names the feature and the timeframe), a history depth outside 1 to 256 — each is
    /// reported with its field (<c>specs[2].p1</c>, <c>specs[2].timeframe</c>, <c>options.historyDepth</c>) in a validation error; nothing is thrown. A date-driven feature on another timeframe than 1m is refused on
    /// <c>specs[i].timeframe</c>; a context feature without <see cref="FrameOptions.Context"/> on <c>options.context</c> and <c>is_expiry_day</c> without <see cref="FrameOptions.ExpiryDays"/> on <c>options.expiryDays</c>
    /// (each reported once, naming the features).
    /// </summary>
    /// <param name="specs">The features to compute; the handle of a spec is its index.</param>
    /// <param name="options">The trading calendar, the history depth, the market context and the expiry dates; the defaults (no calendar, depth 8, no context, no expiry dates) when null.</param>
    public static Result<FeatureFrame> Create(IReadOnlyList<FeatureSpec> specs, FrameOptions? options = null)
    {
        options ??= new FrameOptions();
        if (specs is null || specs.Count == 0)
        {
            return Result.Failure<FeatureFrame>(Error.Validation(InvalidCode, "At least one feature is needed."));
        }

        var errors = new FieldErrors();
        if (options.HistoryDepth is < 1 or > FrameOptions.MaxHistoryDepth)
        {
            errors.Add("options.historyDepth", string.Create(CultureInfo.InvariantCulture, $"The history depth must be between 1 and {FrameOptions.MaxHistoryDepth}."));
        }

        var infos = new IndicatorInfo?[specs.Count];
        List<string>? needContext = null;
        List<string>? needExpiryDays = null;
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var path = "specs[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            if (!IndicatorCatalog.TryGet(spec.Name, out var info))
            {
                errors.Add(path + ".name", string.Create(CultureInfo.InvariantCulture, $"'{spec.Name}' is not a known indicator."));
                continue;
            }

            infos[i] = info;
            info.Validate(spec, path, errors);
            if (info.DateFeature is { } kind)
            {
                if (ContextFeature.ReadsContext(kind) && options.Context is null)
                {
                    Note(ref needContext, info.Name);
                }
                else if (kind == ContextFeatureKind.ExpiryDay && options.ExpiryDays is null)
                {
                    Note(ref needExpiryDays, info.Name);
                }
            }
        }

        if (needContext is not null)
        {
            errors.Add("options.context", "The market context is needed by " + NamesText(needContext) + "; give FrameOptions.Context.");
        }

        if (needExpiryDays is not null)
        {
            errors.Add("options.expiryDays", "The expiry dates are needed by " + NamesText(needExpiryDays) + "; give FrameOptions.ExpiryDays (an empty set is allowed).");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<FeatureFrame>(errors.ToError(InvalidCode, "Some features are invalid."));
        }

        var depth = options.HistoryDepth;
        var handles = new Dictionary<FeatureSpec, int>(specs.Count);
        var shared = new Dictionary<FeatureSpec, (Timeframe Timeframe, int Index)>(specs.Count);
        var laneIndicators = new Dictionary<Timeframe, List<IIndicator>>();
        var dateIndicators = new List<IIndicator>();
        var expiryDays = options.ExpiryDays is null ? null : options.ExpiryDays as FrozenSet<DateOnly> ?? options.ExpiryDays.ToFrozenSet();
        var byHandle = new IIndicator[specs.Count];
        var timeframeOfHandle = new Timeframe[specs.Count];
        var onDate = new bool[specs.Count];
        var indexOfHandle = new int[specs.Count];
        var canonicalSpecs = new FeatureSpec[specs.Count];
        for (var i = 0; i < specs.Count; i++)
        {
            var info = infos[i]!;
            var canonical = info.Canonical(specs[i]);
            canonicalSpecs[i] = canonical;
            onDate[i] = info.DateDriven;
            if (!shared.TryGetValue(canonical, out var placed))
            {
                List<IIndicator>? list;
                if (info.DateDriven)
                {
                    list = dateIndicators;
                }
                else if (!laneIndicators.TryGetValue(canonical.Timeframe, out list))
                {
                    laneIndicators.Add(canonical.Timeframe, list = []);
                }

                placed = (canonical.Timeframe, list.Count);
                list.Add(info.CreateWith(canonical, options.Context, expiryDays));
                shared.Add(canonical, placed);
            }

            byHandle[i] = info.DateDriven ? dateIndicators[placed.Index] : laneIndicators[placed.Timeframe][placed.Index];
            timeframeOfHandle[i] = placed.Timeframe;
            indexOfHandle[i] = placed.Index;
            handles.TryAdd(canonical, i);
        }

        var lanes = new List<Lane>();
        var laneByTimeframe = new Lane?[Enum.GetValues<Timeframe>().Length];
        foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
        {
            // the date-driven features are asked for on 1m, so such a frame has a 1m lane even when no bar feature is on it
            if (laneIndicators.TryGetValue(timeframe, out var list) || (timeframe == Timeframe.M1 && dateIndicators.Count > 0))
            {
                var lane = new Lane(timeframe, timeframe == Timeframe.M1 ? null : new TimeframeBuilder(timeframe, options.Calendar), list is null ? [] : [.. list], depth);
                lanes.Add(lane);
                laneByTimeframe[(int)timeframe] = lane;
            }
        }

        var dateLane = dateIndicators.Count > 0 ? new Lane(Timeframe.M1, null, [.. dateIndicators], depth) : null;
        var laneOfHandle = new Lane[specs.Count];
        var historyBase = new int[specs.Count];
        for (var i = 0; i < specs.Count; i++)
        {
            laneOfHandle[i] = onDate[i] ? dateLane! : laneByTimeframe[(int)timeframeOfHandle[i]]!;
            historyBase[i] = indexOfHandle[i] * depth;
        }

        return Result.Success(new FeatureFrame([.. lanes], dateLane, laneByTimeframe, byHandle, laneOfHandle, historyBase, canonicalSpecs, handles, depth));
    }

    /// <summary>Builds a frame with the default history depth and the trading calendar given to the timeframe builders (see <see cref="FrameOptions"/>).</summary>
    public static Result<FeatureFrame> Create(IReadOnlyList<FeatureSpec> specs, TradingCalendar? calendar) => Create(specs, new FrameOptions(calendar));

    /// <summary>Whether the feature behind the handle has a value now. A date-driven feature can be not ready again on a date the context has a gap for (no VIX that day).</summary>
    public bool IsReady(int handle) => _byHandle[handle].IsReady;

    /// <summary>
    /// The bars of the feature's own timeframe it needs before it is ready (see <see cref="IIndicator.WarmUp"/>). <b>For a date-driven feature</b> (<see cref="IndicatorInfo.DateDriven"/>) the unit is <b>days of the
    /// market context</b>, not bars, and the number is that of the context the frame was given (<c>regime</c> needs 265 daily records with the default options): a simulator sizes the preload of the context in days,
    /// not in 1-minute bars of the feature's timeframe.
    /// </summary>
    public int WarmUp(int handle) => _byHandle[handle].WarmUp;

    /// <summary>The spec behind a handle, in canonical form (a <see cref="PriceSource.Default"/> source resolved, an ignored source close).</summary>
    public FeatureSpec SpecOf(int handle) => _specs[handle];

    /// <summary>The handle of the first spec equal to <paramref name="spec"/> (ignoring a source the indicator does not read), or -1 when the frame was not asked for it.</summary>
    public int HandleOf(in FeatureSpec spec) =>
        IndicatorCatalog.TryGet(spec.Name, out var info) && _handles.TryGetValue(info.Canonical(spec), out var handle) ? handle : -1;

    /// <summary>Reads a feature by its spec: true when the frame computes it and it is ready. Prefer the handle in a hot loop.</summary>
    public bool TryGet(in FeatureSpec spec, out double value)
    {
        var handle = HandleOf(spec);
        if (handle >= 0 && _byHandle[handle].IsReady)
        {
            value = _byHandle[handle].Value;
            return true;
        }

        value = double.NaN;
        return false;
    }

    /// <summary>
    /// The value the feature had <paramref name="lag"/> closed bars of its own timeframe ago: <c>0</c> is the current value (<c>this[handle]</c>), <c>1</c> the value after the previous closed bar, and so on.
    /// <see cref="double.NaN"/> when fewer than <c>lag + 1</c> bars have closed or the feature was not ready then. A lag outside <c>0</c> to <see cref="HistoryDepth"/> − 1 is a programming error and throws.
    /// </summary>
    public double At(int handle, int lag)
    {
        if ((uint)lag >= (uint)_depth)
        {
            throw new ArgumentOutOfRangeException(nameof(lag), lag, "The lag must be between 0 and HistoryDepth - 1.");
        }

        if (lag == 0)
        {
            return _byHandle[handle].Value;
        }

        var lane = _laneOfHandle[handle];
        if (lag >= lane.Recorded)
        {
            return double.NaN;
        }

        var slot = lane.Head - lag;
        if (slot < 0)
        {
            slot += _depth;
        }

        return lane.History[_historyBase[handle] + slot];
    }

    /// <summary>Whether the feature had a value <paramref name="lag"/> closed bars of its own timeframe ago (see <see cref="At"/>).</summary>
    public bool IsReadyAt(int handle, int lag) => lag == 0 ? _byHandle[handle].IsReady : !double.IsNaN(At(handle, lag));

    /// <summary>Whether the frame was asked for a feature on the timeframe (the closed flags are only available for those).</summary>
    public bool HasTimeframe(Timeframe timeframe) => LaneOrNull(timeframe) is not null;

    /// <summary>Whether the last step closed at least one bar of the timeframe (1m: every accepted step). False after a bar the ordering guard skipped. Throws for a timeframe the frame was not asked for.</summary>
    public bool BarClosed(Timeframe timeframe) => LaneFor(timeframe).ClosedNow > 0;

    /// <summary>How many bars of the timeframe the last step closed: 0, 1, or 2 when one minute closed a late bucket and its own. Throws for a timeframe the frame was not asked for.</summary>
    public int ClosedBarsAt(Timeframe timeframe) => LaneFor(timeframe).ClosedNow;

    /// <summary>
    /// The last bar the timeframe has closed so far, <see cref="ClosedBar.Complete"/> flag included (not necessarily at the last step; see <see cref="BarClosed"/>); false while none has closed. Throws for a timeframe
    /// the frame was not asked for.
    /// </summary>
    public bool TryLastClosed(Timeframe timeframe, out ClosedBar bar)
    {
        var lane = LaneFor(timeframe);
        bar = lane.Last;
        return lane.Recorded > 0;
    }

    /// <summary>
    /// Advances the frame by one closed 1-minute bar: the 1-minute features see it at once, a higher timeframe's features see the bars that this minute closed (none while the bucket is still forming). A bar at or
    /// before the previous one is counted in <see cref="IgnoredBars"/> and skipped by every lane.
    /// </summary>
    public void Step(in Bar minuteBar)
    {
        var lanes = _lanes;
        if (_seenBar && minuteBar.EpochSeconds <= _lastEpoch)
        {
            IgnoredBars++;
            for (var l = 0; l < lanes.Length; l++)
            {
                lanes[l].ClosedNow = 0;
            }

            return;
        }

        _seenBar = true;
        _lastEpoch = minuteBar.EpochSeconds;
        Steps++;
        for (var l = 0; l < lanes.Length; l++)
        {
            var lane = lanes[l];
            var builder = lane.Builder;
            if (builder is null)
            {
                lane.ClosedNow = 1;
                lane.Last = new ClosedBar(minuteBar.EpochSeconds, minuteBar, 1, true, Timeframe.M1);
                Consume(lane, minuteBar);
                continue;
            }

            var closed = builder.Push(minuteBar, out var first, out var second);
            lane.ClosedNow = closed;
            if (closed > 0)
            {
                Consume(lane, first);
                lane.Last = first;
                if (closed > 1)
                {
                    Consume(lane, second);
                    lane.Last = second;
                }
            }
        }

        if (_dateLane is { } dateLane)
        {
            var day = IndiaDays.DayOf(minuteBar.EpochSeconds);
            if (!dateLane.DayKnown || day != dateLane.Day)
            {
                dateLane.DayKnown = true;
                dateLane.Day = day;
                Consume(dateLane, minuteBar);
            }
        }
    }

    /// <summary>Returns the frame to the state after <see cref="Create(IReadOnlyList{FeatureSpec}, FrameOptions?)"/>.</summary>
    public void Reset()
    {
        foreach (var lane in _lanes)
        {
            lane.Reset();
        }

        _dateLane?.Reset();
        Steps = 0;
        IgnoredBars = 0;
        IncompleteBarsConsumed = 0;
        _seenBar = false;
        _lastEpoch = 0;
    }

    private static void Note(ref List<string>? names, string name)
    {
        names ??= [];
        if (!names.Contains(name))
        {
            names.Add(name);
        }
    }

    private static string NamesText(List<string> names) => string.Join(", ", names.Select(n => "'" + n + "'"));

    private Lane? LaneOrNull(Timeframe timeframe) => (uint)(int)timeframe < (uint)_laneByTimeframe.Length ? _laneByTimeframe[(int)timeframe] : null;

    private Lane LaneFor(Timeframe timeframe) =>
        LaneOrNull(timeframe) ?? throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "The frame was not asked for a feature on this timeframe.");

    private void Consume(Lane lane, in ClosedBar closed)
    {
        if (!closed.Complete)
        {
            IncompleteBarsConsumed++;
        }

        Consume(lane, closed.Bar);
    }

    /// <summary>Feeds one closed bar of the lane's timeframe to its indicators and records the values they have afterwards.</summary>
    private void Consume(Lane lane, in Bar bar)
    {
        var indicators = lane.Indicators;
        for (var i = 0; i < indicators.Length; i++)
        {
            indicators[i].Update(bar);
        }

        var depth = _depth;
        var head = lane.Head + 1 == depth ? 0 : lane.Head + 1;
        lane.Head = head;
        var history = lane.History;
        for (var i = 0; i < indicators.Length; i++)
        {
            history[i * depth + head] = indicators[i].Value;
        }

        lane.Recorded++;
    }

    /// <summary>The indicators of one timeframe, its builder (none for 1m) and the ring of their recent values. The date lane (the date-driven features) is one too, fed once per India date instead of per closed bar.</summary>
    private sealed class Lane
    {
        private readonly int _depth;

        public Lane(Timeframe timeframe, TimeframeBuilder? builder, IIndicator[] indicators, int depth)
        {
            Timeframe = timeframe;
            Builder = builder;
            Indicators = indicators;
            History = new double[indicators.Length * depth];
            _depth = depth;
            Head = depth - 1;
        }

        public Timeframe Timeframe { get; }

        public TimeframeBuilder? Builder { get; }

        public IIndicator[] Indicators { get; }

        /// <summary>The values after each of the last closed bars: instance <c>i</c> owns <c>History[i * depth .. (i + 1) * depth)</c> as a ring whose newest slot is <see cref="Head"/>.</summary>
        public double[] History { get; }

        public int Head { get; set; }

        /// <summary>How many bars this timeframe has closed (and recorded) so far.</summary>
        public long Recorded { get; set; }

        /// <summary>How many bars the last step closed.</summary>
        public int ClosedNow { get; set; }

        /// <summary>The last bar closed (meaningful once <see cref="Recorded"/> is above 0).</summary>
        public ClosedBar Last { get; set; }

        /// <summary>The India date (day number) the date lane last refreshed for; used by the date lane only.</summary>
        public long Day { get; set; }

        /// <summary>Whether the date lane has refreshed at all (a day number of 0 is a date too).</summary>
        public bool DayKnown { get; set; }

        public void Reset()
        {
            Builder?.Reset();
            foreach (var indicator in Indicators)
            {
                indicator.Reset();
            }

            Array.Clear(History);
            Head = _depth - 1;
            Recorded = 0;
            ClosedNow = 0;
            Last = default;
            Day = 0;
            DayKnown = false;
        }
    }
}
