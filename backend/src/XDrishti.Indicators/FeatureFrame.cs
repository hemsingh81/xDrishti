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
/// </summary>
public sealed class FeatureFrame
{
    public const string InvalidCode = "indicators.invalid_features";

    private readonly Lane[] _lanes;

    private readonly Lane?[] _laneByTimeframe;

    private readonly IIndicator[] _byHandle;

    private readonly Lane[] _laneOfHandle;

    private readonly int[] _historyBase;

    private readonly FeatureSpec[] _specs;

    private readonly Dictionary<FeatureSpec, int> _handles;

    private readonly int _depth;

    private bool _seenBar;

    private long _lastEpoch;

    private FeatureFrame(Lane[] lanes, Lane?[] laneByTimeframe, IIndicator[] byHandle, Lane[] laneOfHandle, int[] historyBase, FeatureSpec[] specs, Dictionary<FeatureSpec, int> handles, int depth)
    {
        _lanes = lanes;
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
    internal int InstanceCount => _lanes.Sum(l => l.Indicators.Length);

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
    /// 1m, 5m, 10m, 15m, 30m, 1h or 1D, an indicator that cannot run on the timeframe, a history depth outside 1 to 256 — each is reported with its field (<c>specs[2].p1</c>, <c>options.historyDepth</c>) in a
    /// validation error; nothing is thrown.
    /// </summary>
    /// <param name="specs">The features to compute; the handle of a spec is its index.</param>
    /// <param name="options">The trading calendar and the history depth; the defaults (no calendar, depth 8) when null.</param>
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
        }

        if (errors.HasErrors)
        {
            return Result.Failure<FeatureFrame>(errors.ToError(InvalidCode, "Some features are invalid."));
        }

        var depth = options.HistoryDepth;
        var handles = new Dictionary<FeatureSpec, int>(specs.Count);
        var shared = new Dictionary<FeatureSpec, (Timeframe Timeframe, int Index)>(specs.Count);
        var laneIndicators = new Dictionary<Timeframe, List<IIndicator>>();
        var byHandle = new IIndicator[specs.Count];
        var timeframeOfHandle = new Timeframe[specs.Count];
        var indexOfHandle = new int[specs.Count];
        var canonicalSpecs = new FeatureSpec[specs.Count];
        for (var i = 0; i < specs.Count; i++)
        {
            var info = infos[i]!;
            var canonical = info.Canonical(specs[i]);
            canonicalSpecs[i] = canonical;
            if (!shared.TryGetValue(canonical, out var placed))
            {
                if (!laneIndicators.TryGetValue(canonical.Timeframe, out var list))
                {
                    laneIndicators.Add(canonical.Timeframe, list = []);
                }

                placed = (canonical.Timeframe, list.Count);
                list.Add(info.Create(canonical));
                shared.Add(canonical, placed);
            }

            byHandle[i] = laneIndicators[placed.Timeframe][placed.Index];
            timeframeOfHandle[i] = placed.Timeframe;
            indexOfHandle[i] = placed.Index;
            handles.TryAdd(canonical, i);
        }

        var lanes = new List<Lane>();
        var laneByTimeframe = new Lane?[Enum.GetValues<Timeframe>().Length];
        foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
        {
            if (laneIndicators.TryGetValue(timeframe, out var list))
            {
                var lane = new Lane(timeframe, timeframe == Timeframe.M1 ? null : new TimeframeBuilder(timeframe, options.Calendar), [.. list], depth);
                lanes.Add(lane);
                laneByTimeframe[(int)timeframe] = lane;
            }
        }

        var laneOfHandle = new Lane[specs.Count];
        var historyBase = new int[specs.Count];
        for (var i = 0; i < specs.Count; i++)
        {
            laneOfHandle[i] = laneByTimeframe[(int)timeframeOfHandle[i]]!;
            historyBase[i] = indexOfHandle[i] * depth;
        }

        return Result.Success(new FeatureFrame([.. lanes], laneByTimeframe, byHandle, laneOfHandle, historyBase, canonicalSpecs, handles, depth));
    }

    /// <summary>Builds a frame with the default history depth and the trading calendar given to the timeframe builders (see <see cref="FrameOptions"/>).</summary>
    public static Result<FeatureFrame> Create(IReadOnlyList<FeatureSpec> specs, TradingCalendar? calendar) => Create(specs, new FrameOptions(calendar));

    /// <summary>Whether the feature behind the handle has a value yet.</summary>
    public bool IsReady(int handle) => _byHandle[handle].IsReady;

    /// <summary>The bars of the feature's own timeframe it needs before it is ready (see <see cref="IIndicator.WarmUp"/>).</summary>
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
    }

    /// <summary>Returns the frame to the state after <see cref="Create(IReadOnlyList{FeatureSpec}, FrameOptions?)"/>.</summary>
    public void Reset()
    {
        foreach (var lane in _lanes)
        {
            lane.Reset();
        }

        Steps = 0;
        IgnoredBars = 0;
        IncompleteBarsConsumed = 0;
        _seenBar = false;
        _lastEpoch = 0;
    }

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

    /// <summary>The indicators of one timeframe, its builder (none for 1m) and the ring of their recent values.</summary>
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
        }
    }
}
