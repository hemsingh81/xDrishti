using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>
/// The reference for the twelve per-session features on an intraday timeframe (ADR 0035), written from the <b>definition</b> and not from the frame: the interpreters of the oracle read these leaves here and nowhere else.
/// <para>
/// The definition (P3-01b criterion 2 as narrowed by ADR 0035): let <c>D</c> be the India date of the last accepted 1-minute bar and <c>S</c> the number of bars the feature's timeframe has closed whose <i>start</i> lies on
/// <c>D</c>. The feature <i>k</i> closed bars ago is the value the indicator had after that bar when <c>k &lt; S</c> and unknown otherwise; with <c>S</c> = 0 (a coarser timeframe that has not closed its first bar of the
/// date, or a late-closed bucket of the date before) nothing is known at any lag. This class does exactly that and nothing else: per feature it runs a <see cref="TimeframeBuilder"/> and the indicator of the catalog over
/// every accepted bar from the start (so the values <i>inside</i> a session come from the same Indicators code as the frame — that is the limit of the independence —, the rule does not), records the start date and the
/// value of each closed bar, and masks a read by counting <c>S</c> from those start dates (<see cref="SessionClock.DateOf"/> of the Domain). It has no clearing, no pending flag and no date bookkeeping of the frame's, and it
/// does not use <see cref="FeatureFrame"/> at all. The ordering guard is written again here: a bar is accepted when it is later than the last accepted one.
/// </para>
/// <para>
/// With <c>runOn</c> the mask is dropped: a read is by position, as the frame was read before ADR 0035. That is the negative control — the interpreters fed by it must disagree with the compiled conditions
/// wherever the rule matters — and it is not a never-reset <i>frame</i>, which would carry the rule in itself.
/// </para>
/// <para>
/// <see cref="Feed"/> after every <c>frame.Step</c> and <see cref="Reset"/> with <c>frame.Reset()</c>. An interpreter that meets a covered leaf without a reference throws.
/// </para>
/// </summary>
internal sealed class SessionReference
{
    /// <summary>The twelve features that restart each session, written out here (the oracle's own list; a test pins it to the catalog).</summary>
    public static readonly string[] Names =
    [
        "vwap", "or_high", "or_low", "today_open", "today_high", "today_low", "prev_day_open", "prev_day_high", "prev_day_low", "prev_day_close", "prev_day_volume", "gap_pct",
    ];

    private const int Kept = 600;

    private readonly Dictionary<FeatureSpec, Track> _tracks = [];

    private readonly bool _runOn;

    private bool _any;

    private long _lastEpoch;

    private DateOnly _date;

    /// <summary>The features of the frame that the reference covers (the others are left to the frame).</summary>
    public SessionReference(IEnumerable<FeatureSpec> features, TradingCalendar? calendar = null, bool runOn = false)
    {
        _runOn = runOn;
        foreach (var spec in features.Where(Covers).Distinct())
        {
            _tracks.Add(spec, new Track(spec, calendar));
        }
    }

    /// <summary>A reference for the features a frame computes.</summary>
    public static SessionReference For(FeatureFrame frame, TradingCalendar? calendar = null, bool runOn = false) =>
        new(Enumerable.Range(0, frame.Count).Select(frame.SpecOf), calendar, runOn);

    /// <summary>Whether the feature is read from the reference: one of the twelve names on a timeframe with minutes (1m to 1h). On 1D nothing restarts, on 1W and 1M the names are refused.</summary>
    public static bool Covers(FeatureSpec spec) => Names.Contains(spec.Name) && spec.Timeframe.Minutes() > 0;

    /// <summary>How many reads were turned unknown, by the cause: the lane had bars of the date but the read reached before the first of them, or the lane had none of the date yet (pending). Zero for the run-on control.</summary>
    public (long History, long Pending) Masked { get; private set; }

    /// <summary>How many features the reference covers.</summary>
    public int Count => _tracks.Count;

    /// <summary>Takes the 1-minute bar the frame has just been given.</summary>
    public void Feed(in Bar bar)
    {
        if (_any && bar.EpochSeconds <= _lastEpoch)
        {
            return;
        }

        _any = true;
        _lastEpoch = bar.EpochSeconds;
        _date = SessionClock.DateOf(bar.EpochSeconds);
        foreach (var track in _tracks.Values)
        {
            track.Feed(bar);
        }
    }

    /// <summary>Forgets every bar, as <c>frame.Reset()</c> does.</summary>
    public void Reset()
    {
        _any = false;
        _lastEpoch = 0;
        foreach (var track in _tracks.Values)
        {
            track.Reset();
        }

        Masked = (0, 0);
    }

    /// <summary>The value of the feature <paramref name="lag"/> closed bars of its own timeframe ago; NaN when it is not known by the definition.</summary>
    public double Read(FeatureSpec spec, int lag)
    {
        if (!_tracks.TryGetValue(spec, out var track))
        {
            throw new InvalidOperationException("The reference does not cover " + spec + ": it was not given to it.");
        }

        if (!_any)
        {
            return double.NaN;
        }

        var closed = track.Count;
        var sessionBars = track.BarsOf(_date);
        var positional = lag < closed ? track.Value(lag) : double.NaN;
        if (_runOn)
        {
            return positional;
        }

        if (lag >= sessionBars)
        {
            if (!double.IsNaN(positional))
            {
                Masked = sessionBars == 0 ? (Masked.History, Masked.Pending + 1) : (Masked.History + 1, Masked.Pending);
            }

            return double.NaN;
        }

        return positional;
    }

    /// <summary>One feature: its builder, its indicator and the start date and value after each of the last closed bars.</summary>
    private sealed class Track
    {
        private readonly FeatureSpec _spec;

        private readonly TradingCalendar? _calendar;

        private readonly List<(DateOnly Date, double Value)> _closed = [];

        private TimeframeBuilder? _builder;

        private IIndicator _indicator;

        public Track(FeatureSpec spec, TradingCalendar? calendar)
        {
            _spec = spec;
            _calendar = calendar;
            _builder = Builder();
            _indicator = Create();
        }

        public int Count => _closed.Count;

        public double Value(int lag) => _closed[^(lag + 1)].Value;

        /// <summary>How many of the newest closed bars start on <paramref name="date"/>.</summary>
        public int BarsOf(DateOnly date)
        {
            var n = 0;
            while (n < _closed.Count && _closed[^(n + 1)].Date == date)
            {
                n++;
            }

            return n;
        }

        public void Feed(in Bar bar)
        {
            if (_builder is null)
            {
                // a 1-minute bar closes itself
                Take(new ClosedBar(bar.EpochSeconds, bar, 1, true, Timeframe.M1));
                return;
            }

            var count = _builder.Push(bar, out var first, out var second);
            if (count > 0)
            {
                Take(first);
                if (count > 1)
                {
                    Take(second);
                }
            }
        }

        public void Reset()
        {
            _closed.Clear();
            _builder = Builder();
            _indicator = Create();
        }

        private TimeframeBuilder? Builder() => _spec.Timeframe == Timeframe.M1 ? null : new TimeframeBuilder(_spec.Timeframe, _calendar);

        private IIndicator Create() => IndicatorCatalog.TryGet(_spec.Name, out var info) ? info.Create(_spec) : throw new InvalidOperationException("No indicator " + _spec.Name);

        private void Take(in ClosedBar closed)
        {
            _indicator.Update(closed.Bar);
            _closed.Add((SessionClock.DateOf(closed.StartEpochSeconds), _indicator.Value));
            if (_closed.Count > 2 * Kept)
            {
                _closed.RemoveRange(0, _closed.Count - Kept);
            }
        }
    }
}
