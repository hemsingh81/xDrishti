using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// The first stage of the simulator (spec P3-03a, D1, D2, D10): steps a feature frame with the 1-minute bars of one instrument and, at the steps where a bar of the evaluation timeframe closed, asks the bound side whether
/// the entry condition holds and makes an <see cref="EntryCandidate"/> (at most one per instrument, side and date: the first). It knows nothing of exits: the candidate carries a stop level and nothing else, so the same
/// entries can be walked under any exit policy.
/// <para>
/// Decisions are taken only at a step where the evaluation timeframe closed a bar. A step that closed two bars decides the second and counts the first as lost; a step whose last closed bar started on an earlier India
/// date than the step's own minute (a bucket closed late by the first minute of the next date) is stale: counted, not evaluated, because no order may be placed on yesterday's bar. The date of a step is that of its own
/// minute. All handles are resolved once in <see cref="Create"/>; a step allocates nothing unless a candidate is made. Not thread-safe: one scanner belongs to one thread, like its frame.
/// </para>
/// </summary>
public sealed class EntryScanner
{
    private readonly CompiledSide _side;
    private readonly BoundSide _bound;
    private readonly FeatureFrame _frame;
    private readonly InstrumentRef _instrument;
    private readonly Timeframe _timeframe;
    private readonly int _levelHandle;

    // per item, in the order of CompiledSide.Items
    private readonly int[][] _itemHandles;
    private readonly Truth[] _truths;
    private readonly long[] _true;
    private readonly long[] _false;
    private readonly long[] _unknown;
    private readonly long[] _notReady;
    private readonly int _setupCount;
    private readonly int _invalidateFrom;
    private readonly int[] _readyHandles;
    private readonly string[][] _featureNames;

    private readonly List<EntryCandidate> _candidates = [];

    // P4-01 (D1 to D3): the handles of the features some candidate exit reads, and the tape they are recorded on; empty (and shared) when no exit reads one.
    private readonly int[] _tapeHandles;
    private readonly double[] _tapeRow;

    private int _stepCalls;
    private long _currentDay = long.MinValue;
    private bool _latched;
    private bool _invalidatedToday;
    private bool _hasCandidateToday;

    private long _decisionSteps;
    private long _lostDecisions;
    private long _staleBucketSteps;
    private long _outOfWindowSteps;
    private long _windowSteps;
    private long _fullyReadySteps;
    private long _laterTriggers;
    private long _blockedByInvalidation;
    private long _invalidatedDays;
    private long _stopNotReady;
    private long _unknownInvalidation;

    private EntryScanner(CompiledSide side, BoundSide bound, FeatureFrame frame, InstrumentRef instrument, int levelHandle)
    {
        _side = side;
        _bound = bound;
        _frame = frame;
        _instrument = instrument;
        _timeframe = side.EvaluationTimeframe;
        _levelHandle = levelHandle;
        Candidates = _candidates.AsReadOnly();

        var items = side.Items;
        _itemHandles = new int[items.Count][];
        _featureNames = new string[items.Count][];
        for (var i = 0; i < items.Count; i++)
        {
            var features = items[i].Condition.Features;
            _itemHandles[i] = new int[features.Count];
            _featureNames[i] = new string[features.Count];
            for (var f = 0; f < features.Count; f++)
            {
                _itemHandles[i][f] = frame.HandleOf(features[f]);
                _featureNames[i][f] = FeatureNames.Of(features[f]);
            }
        }

        _truths = new Truth[items.Count];
        _true = new long[items.Count];
        _false = new long[items.Count];
        _unknown = new long[items.Count];
        _notReady = new long[items.Count];
        _setupCount = side.Setup?.Items.Count ?? 0;
        _invalidateFrom = _setupCount + side.Trigger.Items.Count;
        _readyHandles = [.. Enumerable.Range(0, _invalidateFrom).SelectMany(i => _itemHandles[i]).Distinct()];
        var reads = side.Exits.SelectMany(e => e.Reads).Distinct().ToArray();
        _tapeHandles = [.. reads.Select(r => frame.HandleOf(r))];
        _tapeRow = new double[_tapeHandles.Length];
        Tape = reads.Length == 0 ? ExitTape.Empty : new ExitTape([.. reads.Select(r => FeatureNames.Of(r))]);
    }

    /// <summary>
    /// The exit tape (P4-01): one row for each step at which the evaluation timeframe closed a bar, with the features the exits of the side read as of that close; <see cref="ExitTape.Empty"/> when no exit reads one, which is every
    /// strategy today. A step that closed two bars gives one row, with the later bar's values.
    /// </summary>
    public ExitTape Tape { get; }

    /// <summary>The candidates made so far, in the order of their decisions.</summary>
    public IReadOnlyList<EntryCandidate> Candidates { get; }

    /// <summary>What the scanner counted so far (a snapshot; D10).</summary>
    public ScanCounters Counters
    {
        get
        {
            var conditions = new ConditionCounter[_truths.Length];
            for (var i = 0; i < conditions.Length; i++)
            {
                conditions[i] = new ConditionCounter(_side.Items[i].Path, _true[i], _false[i], _unknown[i], _notReady[i]);
            }

            return new ScanCounters(
                _side.Side,
                _frame.Steps,
                _frame.IgnoredBars,
                _frame.IncompleteBarsConsumed,
                _decisionSteps,
                _lostDecisions,
                _staleBucketSteps,
                _outOfWindowSteps,
                _windowSteps,
                _fullyReadySteps,
                _candidates.Count,
                _laterTriggers,
                _blockedByInvalidation,
                _invalidatedDays,
                _stopNotReady,
                _unknownInvalidation,
                Array.AsReadOnly(conditions));
        }
    }

    /// <summary>
    /// Makes the scanner of a compiled side for one instrument: its own frame, created from the features of the side with the calendar (a special session then closes its last buckets at its own last minute), and the side
    /// bound to it. The stop the scanner reads is the level feature of <c>swing_low</c>, <c>swing_high</c>, <c>bar_low</c>, <c>bar_high</c> and <c>or_opposite</c> on the evaluation timeframe with no buffer; the rest, and an
    /// evaluation on a week or a month, are refused by name. A 1D evaluation is accepted (ADR 0043): its decision is taken at the step of the 15:29 minute.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="side"/> is null.</exception>
    public static Result<EntryScanner> Create(CompiledSide side, InstrumentRef instrument, TradingCalendar? calendar = null)
    {
        ArgumentNullException.ThrowIfNull(side);
        if (side.EvaluationTimeframe >= Timeframe.W1)
        {
            return Result.Failure<EntryScanner>(Error.Validation(ScannerCodes.TimeframeUnsupported, "The scanner does not evaluate on " + side.EvaluationTimeframe.ToCode() + ": the frame closes a 1D bar at the step of the 15:29 minute (a bar closed late by the first minute of the next date is stale and not evaluated), but a bar of a week or a month is not evaluated (ADR 0043)."));
        }

        if (StopProblem(side.Stop, side.EvaluationTimeframe) is { } problem)
        {
            return Result.Failure<EntryScanner>(Error.Validation(ScannerCodes.StopUnsupported, problem));
        }

        var created = FeatureFrame.Create(side.Features, new FrameOptions(calendar, Math.Max(side.HistoryDepth, 1)));
        if (created.IsFailure)
        {
            return Result.Failure<EntryScanner>(created.Error);
        }

        var frame = created.Value;
        var bound = side.Bind(frame);
        return Result.Success(new EntryScanner(side, bound, frame, instrument, frame.HandleOf(side.Stop.Level!.Value)));
    }

    /// <summary>
    /// Feeds one 1-minute bar. Minutes must ascend: a bar at or before the previous one is counted in <c>IgnoredBars</c> and skipped (a <c>Simulator.Run</c> checks the order first). Allocates nothing unless a candidate is made.
    /// </summary>
    public void Step(in Bar minuteBar)
    {
        var index = _stepCalls++;
        var steps = _frame.Steps;
        _frame.Step(minuteBar);
        if (_frame.Steps == steps)
        {
            return;
        }

        var day = SessionClock.DateOf(minuteBar.EpochSeconds).DayNumber;
        if (day != _currentDay)
        {
            _currentDay = day;
            _latched = false;
            _invalidatedToday = false;
            _hasCandidateToday = false;
        }

        if (!_frame.BarClosed(_timeframe))
        {
            return;
        }

        _decisionSteps++;
        var closedNow = _frame.ClosedBarsAt(_timeframe);
        if (closedNow > 1)
        {
            _lostDecisions += closedNow - 1;
        }

        _frame.TryLastClosed(_timeframe, out var closed);
        if (_tapeHandles.Length > 0)
        {
            for (var f = 0; f < _tapeHandles.Length; f++)
            {
                _tapeRow[f] = _frame[_tapeHandles[f]];
            }

            Tape.Append(_frame.LastBarTime + 60, closed.Bar.Close, _tapeRow);
        }

        if (SessionClock.DateOf(closed.StartEpochSeconds).DayNumber < day)
        {
            _staleBucketSteps++;
            return;
        }

        var evaluation = _bound.Evaluate(_frame, _truths);
        Count(_invalidateFrom, _truths.Length);
        if (evaluation.Invalidate == Truth.True)
        {
            _latched = true;
            if (!_invalidatedToday)
            {
                _invalidatedToday = true;
                _invalidatedDays++;
            }
        }

        if (!_bound.InEntryWindow(_frame))
        {
            _outOfWindowSteps++;
            return;
        }

        _windowSteps++;
        Count(0, _invalidateFrom);
        if (AllReady())
        {
            _fullyReadySteps++;
        }

        if (evaluation.Setup != Truth.True || evaluation.Trigger != Truth.True)
        {
            return;
        }

        if (_hasCandidateToday)
        {
            _laterTriggers++;
        }
        else if (_latched)
        {
            _blockedByInvalidation++;
        }
        else if (evaluation.Invalidate == Truth.Unknown)
        {
            _unknownInvalidation++;
        }
        else if (double.IsNaN(_frame[_levelHandle]))
        {
            _stopNotReady++;
        }
        else
        {
            _hasCandidateToday = true;
            _candidates.Add(MakeCandidate(index, closed));
        }
    }

    private static string? StopProblem(StopPlan stop, Timeframe evaluation)
    {
        var name = StopName(stop.Kind);
        if (stop.Kind is not (StopKind.SwingLow or StopKind.SwingHigh or StopKind.BarLow or StopKind.BarHigh or StopKind.OrOpposite) || stop.Level is null)
        {
            return "The scanner reads a stop that is a level (swing_low, swing_high, bar_low, bar_high, or_opposite), not `" + name + "`.";
        }

        if (stop.BufferAtr > 0)
        {
            return "The scanner does not apply a buffer of ATRs to the stop (`buffer_atr` " + stop.BufferAtr.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " on `" + name + "`).";
        }

        if (stop.Timeframe != evaluation)
        {
            return "The scanner reads the stop on the evaluation timeframe " + evaluation.ToCode() + ", not " + stop.Timeframe.ToCode() + ".";
        }

        return null;
    }

    private static string StopName(StopKind kind) => kind switch
    {
        StopKind.FixedPct => "fixed_pct",
        StopKind.AtrMult => "atr_mult",
        StopKind.SwingLow => "swing_low",
        StopKind.SwingHigh => "swing_high",
        StopKind.BarLow => "bar_low",
        StopKind.BarHigh => "bar_high",
        StopKind.OrOpposite => "or_opposite",
        _ => throw new InvalidOperationException("Not a stop kind."),
    };

    private void Count(int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            switch (_truths[i])
            {
                case Truth.True:
                    _true[i]++;
                    break;
                case Truth.False:
                    _false[i]++;
                    break;
                default:
                    if (AnyEmpty(_itemHandles[i]))
                    {
                        _notReady[i]++;
                    }
                    else
                    {
                        _unknown[i]++;
                    }

                    break;
            }
        }
    }

    private bool AnyEmpty(int[] handles)
    {
        for (var h = 0; h < handles.Length; h++)
        {
            if (double.IsNaN(_frame[handles[h]]))
            {
                return true;
            }
        }

        return false;
    }

    private bool AllReady() => !AnyEmpty(_readyHandles);

    private EntryCandidate MakeCandidate(int decisionIndex, ClosedBar closed)
    {
        var items = new TraceItem[_truths.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var handles = _itemHandles[i];
            var depth = _side.Items[i].Condition.HistoryDepth;
            var values = new TraceValue[handles.Length];
            for (var f = 0; f < handles.Length; f++)
            {
                var lags = new double[depth];
                for (var lag = 0; lag < depth; lag++)
                {
                    lags[lag] = _frame.At(handles[f], lag);
                }

                values[f] = new TraceValue(_featureNames[i][f], lags);
            }

            var truth = _truths[i] switch
            {
                Truth.True => TraceTruth.True,
                Truth.False => TraceTruth.False,
                _ => TraceTruth.Unknown,
            };
            items[i] = new TraceItem(_side.Items[i].Path, _side.Items[i].Condition.Source, truth, values);
        }

        return new EntryCandidate(
            _side.StrategyId,
            _instrument,
            _side.Side,
            decisionIndex,
            _frame.LastBarTime + 60,
            _timeframe,
            closed.Bar,
            closed.Bars,
            closed.Complete,
            _frame[_levelHandle],
            new DecisionTrace(items));
    }
}
