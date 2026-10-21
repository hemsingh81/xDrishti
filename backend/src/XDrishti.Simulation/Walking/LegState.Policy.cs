using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// The part of <see cref="LegState"/> that the policies of P4-02 add (ADR 0050): T1 of <c>PARTIAL_BE</c> and <c>PARTIAL_TRAIL</c>, the break-even stop, T2, the trail since T1 and the intraday count of the <c>time_stop</c> overlay.
/// T1 is the target of the position, so the minute table of <see cref="MinuteRule"/> decides it like any target (X3); the loops call <see cref="FillT1"/> instead of leaving when the target they hit is the first one of a partial
/// policy. The minute that fills T1 is never also a break-even stop, a T2 or a trail level (D2): the levels move here, and the loops read them from the next minute on.
/// </summary>
internal sealed partial class LegState
{
    private LegPolicy? _policy;
    private bool _t1Filled;
    private long _t1Epoch;
    private decimal _t1Extreme;
    private int _barsRead;
    private bool _timeStopSpent;

    /// <summary>T1 has not filled yet: a target hit by the minute table is the partial exit, not the end of the trade.</summary>
    public bool T1Pending { get; private set; }

    /// <summary>The check of the time stop found too little progress: the open share leaves at the open of the minute that sees it (D8).</summary>
    public bool TimeStopDue { get; private set; }

    /// <summary>The swing time stop in sessions, or 0 for none (D9). It works only with <c>MinProgressRr</c>: <c>SwingSessions</c> alone, without a minimum progress, is no time stop.</summary>
    public int TimeStopSessions => _policy is { MinProgressRr: not null } policy ? policy.SwingSessions : 0;

    /// <summary>The progress below which the time stop exits, or null for none.</summary>
    public decimal? MinProgressRr => _policy?.MinProgressRr;

    /// <summary>Takes the share booked at T1 at the target of the position, a resting limit (no slippage), and moves the levels from the next minute on: <c>PARTIAL_BE</c> the stop to the entry and the target to T2, <c>PARTIAL_TRAIL</c> no target (D2, D5).</summary>
    public void FillT1(in Minute minute, long minuteEpoch)
    {
        var position = _position;
        var t1 = position.Target.GetValueOrDefault();
        var filled = position.Slip.Exit(position.IsBuy, ExitReason.Target, t1, minute.High - minute.Low);
        var book = _policy!.Book;
        _partials.Add(new PartialLeg(minuteEpoch, t1, filled, book, T1Code));
        OpenShare -= book;
        T1Pending = false;
        _t1Filled = true;
        _t1Epoch = minuteEpoch;
        _t1Extreme = position.IsBuy ? minute.High : minute.Low;
        _position = _policy.Partial == PartialKind.Be
            ? position with { Stop = position.Entry, Target = position.Entry + ((position.IsBuy ? 1m : -1m) * _policy.T2Rr * position.Risk) }
            : position with { Target = null };
    }

    /// <summary>Records the extreme of a minute that holds a trade since the T1 fill: the anchor of the trail (D5). Nothing before T1. Called by <see cref="Observe"/>.</summary>
    private void ObservePolicy(in Minute minute)
    {
        if (_t1Filled)
        {
            _t1Extreme = _position.IsBuy ? Math.Max(_t1Extreme, minute.High) : Math.Min(_t1Extreme, minute.Low);
        }
    }

    /// <summary>The code of the T1 leg.</summary>
    public const string T1Code = "T1";

    private void StartPolicy()
    {
        _policy = _context.Policy;
        T1Pending = _policy is { Partial: not PartialKind.None };
        StartPyramid();
    }

    private void ReadPolicy(ExitTape tape, int bar, List<ExitAction> actions)
    {
        if (_policy is not { } policy)
        {
            return;
        }

        var sign = _position.IsBuy ? 1m : -1m;
        if (policy.Partial == PartialKind.Trail && _t1Filled && tape.Features.Count > 0 && TickGrid.TryConvert(tape.Value(0, bar), out var atr))
        {
            // D5: the chandelier since T1; a NaN ATR gives no level; the walker keeps the stop only if the level is tighter.
            actions.Add(new ExitAction(ExitActionKind.MoveStop, _t1Extreme - (sign * policy.TrailMult * atr)));
        }

        ReadPyramid(tape, bar, sign);

        // D8: the flag set here is ignored by the walker when the open of the next minute is beyond the stop or the target; the table fills those rows instead.
        if (policy.MinProgressRr is { } minimum && policy.IntradayBars > 0 && !_timeStopSpent && ++_barsRead == policy.IntradayBars)
        {
            // D8: the check at the close of the N-th bar read (N-th bar that closes after the fill minute); a close that is not a number is no check.
            _timeStopSpent = true;
            TimeStopDue = TickGrid.TryConvert(tape.Close(bar), out var close) && sign * (close - _position.Entry) < minimum * _position.Risk;
        }
    }
}
