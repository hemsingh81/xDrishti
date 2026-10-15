using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators;

/// <summary>
/// The DSL's view of the market at one 1-minute step (ADR 0031): for every requested <see cref="FeatureSpec"/> the value as of the <b>last closed bar of its timeframe</b> — never the bar still forming. The frame
/// is given the specs once, builds only the <see cref="TimeframeBuilder"/>s and indicators they need (two specs for the same feature share one instance) and then advances with <see cref="Step"/>, one closed
/// 1-minute bar at a time. Values are read by the numeric handle of a spec (its position in the list given to <see cref="Create"/>), which is allocation-free.
/// <para>
/// Incomplete higher-timeframe bars (a minute was missing) are consumed like any other and counted in <see cref="IncompleteBarsConsumed"/>. Thread-confined: one frame per instrument and worker.
/// </para>
/// </summary>
public sealed class FeatureFrame
{
    public const string InvalidCode = "indicators.invalid_features";

    private readonly Lane[] _lanes;

    private readonly IIndicator[] _byHandle;

    private readonly FeatureSpec[] _specs;

    private readonly Dictionary<FeatureSpec, int> _handles;

    private FeatureFrame(Lane[] lanes, IIndicator[] byHandle, FeatureSpec[] specs, Dictionary<FeatureSpec, int> handles)
    {
        _lanes = lanes;
        _byHandle = byHandle;
        _specs = specs;
        _handles = handles;
    }

    /// <summary>The number of handles (the specs given to <see cref="Create"/>, duplicates included).</summary>
    public int Count => _byHandle.Length;

    /// <summary>How many distinct indicator instances run (specs for the same feature share one).</summary>
    internal int InstanceCount => _lanes.Sum(l => l.Indicators.Length);

    /// <summary>How many timeframe builders run (one per higher timeframe asked for).</summary>
    internal int BuilderCount => _lanes.Count(l => l.Builder is not null);

    /// <summary>The number of 1-minute bars stepped so far.</summary>
    public long Steps { get; private set; }

    /// <summary>The number of incomplete higher-timeframe bars (a minute was missing) the indicators have consumed.</summary>
    public long IncompleteBarsConsumed { get; private set; }

    /// <summary>The value of the feature as of the last closed bar of its timeframe; <see cref="double.NaN"/> while it is not ready.</summary>
    public double this[int handle] => _byHandle[handle].Value;

    /// <summary>
    /// Validates the specs before any bar is fed and builds the frame. An unknown indicator, a parameter outside its range, a timeframe other than 1m, 5m, 10m, 15m, 30m, 1h or 1D, an indicator that cannot run on
    /// the timeframe — each is reported with its field (<c>specs[2].p1</c>) in a validation error; nothing is thrown.
    /// </summary>
    /// <param name="specs">The features to compute; the handle of a spec is its index.</param>
    /// <param name="calendar">Optional trading calendar, passed to the timeframe builders (a special session then closes its last buckets at its own last minute).</param>
    public static Result<FeatureFrame> Create(IReadOnlyList<FeatureSpec> specs, TradingCalendar? calendar = null)
    {
        if (specs is null || specs.Count == 0)
        {
            return Result.Failure<FeatureFrame>(Error.Validation(InvalidCode, "At least one feature is needed."));
        }

        var errors = new FieldErrors();
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

        var handles = new Dictionary<FeatureSpec, int>(specs.Count);
        var shared = new Dictionary<FeatureSpec, IIndicator>(specs.Count);
        var laneIndicators = new Dictionary<Timeframe, List<IIndicator>>();
        var byHandle = new IIndicator[specs.Count];
        var canonicalSpecs = new FeatureSpec[specs.Count];
        for (var i = 0; i < specs.Count; i++)
        {
            var info = infos[i]!;
            var canonical = info.Canonical(specs[i]);
            canonicalSpecs[i] = canonical;
            if (!shared.TryGetValue(canonical, out var indicator))
            {
                indicator = info.Create(canonical);
                shared.Add(canonical, indicator);
                if (!laneIndicators.TryGetValue(canonical.Timeframe, out var list))
                {
                    laneIndicators.Add(canonical.Timeframe, list = []);
                }

                list.Add(indicator);
            }

            byHandle[i] = indicator;
            handles.TryAdd(canonical, i);
        }

        var lanes = new List<Lane>();
        foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
        {
            if (laneIndicators.TryGetValue(timeframe, out var list))
            {
                lanes.Add(new Lane(timeframe == Timeframe.M1 ? null : new TimeframeBuilder(timeframe, calendar), [.. list]));
            }
        }

        return Result.Success(new FeatureFrame([.. lanes], byHandle, canonicalSpecs, handles));
    }

    /// <summary>Whether the feature behind the handle has a value yet.</summary>
    public bool IsReady(int handle) => _byHandle[handle].IsReady;

    /// <summary>The bars of the feature's own timeframe it needs before it is ready (see <see cref="IIndicator.WarmUp"/>).</summary>
    public int WarmUp(int handle) => _byHandle[handle].WarmUp;

    /// <summary>The spec behind a handle, in canonical form.</summary>
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
    /// Advances the frame by one closed 1-minute bar: the 1-minute features see it at once, a higher timeframe's features see the bars that this minute closed (none while the bucket is still forming).
    /// </summary>
    public void Step(in Bar minuteBar)
    {
        Steps++;
        var lanes = _lanes;
        for (var l = 0; l < lanes.Length; l++)
        {
            var lane = lanes[l];
            var builder = lane.Builder;
            if (builder is null)
            {
                Feed(lane.Indicators, minuteBar);
                continue;
            }

            var closed = builder.Push(minuteBar, out var first, out var second);
            if (closed > 0)
            {
                Consume(lane.Indicators, first);
                if (closed > 1)
                {
                    Consume(lane.Indicators, second);
                }
            }
        }
    }

    /// <summary>Returns the frame to the state after <see cref="Create"/>.</summary>
    public void Reset()
    {
        foreach (var lane in _lanes)
        {
            lane.Builder?.Reset();
            foreach (var indicator in lane.Indicators)
            {
                indicator.Reset();
            }
        }

        Steps = 0;
        IncompleteBarsConsumed = 0;
    }

    private static void Feed(IIndicator[] indicators, in Bar bar)
    {
        for (var i = 0; i < indicators.Length; i++)
        {
            indicators[i].Update(bar);
        }
    }

    private void Consume(IIndicator[] indicators, in ClosedBar closed)
    {
        if (!closed.Complete)
        {
            IncompleteBarsConsumed++;
        }

        Feed(indicators, closed.Bar);
    }

    private sealed record Lane(TimeframeBuilder? Builder, IIndicator[] Indicators);
}
