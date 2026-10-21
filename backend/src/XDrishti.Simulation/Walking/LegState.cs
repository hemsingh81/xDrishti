using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// The position of a walk that has an <see cref="IExitRule"/> (P4-01, D4 to D6): the stop and the target as they stand, the share still open and the partial exits so far. It exists only when a rule does, so the <c>FIXED_RR</c> walk
/// allocates nothing for it. Before each minute that holds a trade it feeds the rule the bars that closed at or before the minute's start and had not been seen (D2), applies what the rule asks in order, and takes the shares it asked to
/// leave at the open of that minute, unless the open is already through the stop, in which case the stop row takes everything (D6). A level a rule moved is in force from this minute on, never inside the minute that closed its bar.
/// </summary>
internal sealed partial class LegState
{
    private readonly ExitContext _context;
    private readonly List<ExitAction> _actions = [];
    private readonly List<ExitAction> _pending = [];
    private readonly List<PartialLeg> _partials = [];
    private Position _position;
    private int _cursor;
    private readonly int _firstBar;
    private decimal _extreme;
    private bool _observed;

    public LegState(in Position start, ExitContext context, AddRules add = default)
    {
        _context = context;
        _add = add;
        _position = start;
        _cursor = context.Tape.LastClosedAtOrBefore(start.FillEpoch);
        _firstBar = _cursor + 1;
        StartPolicy();
    }

    /// <summary>A rule asked for the whole position to leave at the next open (P4-03, D7): set by the last <see cref="Read"/>.</summary>
    public bool SignalExit { get; private set; }

    /// <summary>Records a minute the walk has finished with, for the anchor of a trail (P4-03, D2): the highest high of a buy, the lowest low of a sell, from the fill minute on.</summary>
    public void Observe(in Minute minute)
    {
        var favourable = _position.IsBuy ? minute.High : minute.Low;
        _extreme = !_observed ? favourable : (_position.IsBuy ? Math.Max(_extreme, favourable) : Math.Min(_extreme, favourable));
        _observed = true;
        ObservePolicy(minute);
    }

    /// <summary>The position with the stop and target as they stand.</summary>
    public ref readonly Position Current => ref _position;

    /// <summary>The partial exits so far, in order.</summary>
    public IReadOnlyList<PartialLeg> Partials => _partials;

    /// <summary>The share of the maximum quantity still open.</summary>
    public decimal OpenShare { get; private set; } = 1m;

    /// <summary>The requests to loosen a stop that were ignored.</summary>
    public int StopLoosenIgnored { get; private set; }

    /// <summary>Reads the bars that closed at or before <paramref name="minuteEpoch"/> and were not read yet, moves the levels as the rule asks and keeps the shares it asked to leave until <see cref="Take"/>.</summary>
    public void Read(long minuteEpoch)
    {
        var tape = _context.Tape;
        var last = tape.LastClosedAtOrBefore(minuteEpoch);
        _pending.Clear();
        SignalExit = false;
        if (last <= _cursor)
        {
            return;
        }

        var isBuy = _position.IsBuy;
        while (_cursor < last)
        {
            _cursor++;
            var pendingShare = 0m;
            foreach (var asked in _pending)
            {
                pendingShare += asked.Value;
            }

            var view = new ExitView(isBuy, _position.Entry, _position.Risk, _position.Stop, _position.Target, OpenShare - pendingShare, _partials.Count + _pending.Count, new TrailInput(_extreme, _firstBar));
            _actions.Clear();
            _context.Rule?.OnBar(view, tape, _cursor, _actions);
            ReadPolicy(tape, _cursor, _actions);
            foreach (var action in _actions)
            {
                switch (action.Kind)
                {
                    case ExitActionKind.MoveStop:
                        if (isBuy ? action.Value >= _position.Stop : action.Value <= _position.Stop)
                        {
                            _position = _position with { Stop = action.Value };
                        }
                        else
                        {
                            StopLoosenIgnored++;
                        }

                        break;
                    case ExitActionKind.MoveTarget:
                        _position = _position with { Target = action.Value };
                        break;
                    case ExitActionKind.SignalExit:
                        SignalExit = true;
                        break;
                    default:
                        if (action.Value > 0m && action.Value < OpenShare - pendingShare)
                        {
                            _pending.Add(action);
                            pendingShare += action.Value;
                        }

                        break;
                }
            }
        }
    }

    /// <summary>
    /// Takes the shares the rule asked to leave at the open of <paramref name="minute"/>, unless that open is already through the stop (<paramref name="openThroughStop"/>), in which case the stop row takes everything (D6).
    /// </summary>
    public void Take(in Minute minute, long minuteEpoch, bool openThroughStop)
    {
        if (_pending.Count == 0 || openThroughStop)
        {
            return;
        }

        var isBuy = _position.IsBuy;
        var range = minute.High - minute.Low;
        foreach (var asked in _pending)
        {
            // A market-type fill at the open of the minute (S1 to S3): the slippage of a square-off.
            var filled = _position.Slip.Exit(isBuy, ExitReason.SquareOff, minute.Open, range);
            _partials.Add(new PartialLeg(minuteEpoch, minute.Open, filled, asked.Value, asked.Code));
            OpenShare -= asked.Value;
        }
    }
}
