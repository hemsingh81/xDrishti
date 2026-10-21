using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// The part of <see cref="LegState"/> that <c>PYRAMID</c> adds (P4-04, ADR 0052): the entry legs, the ladder, the add that a closed bar asks for and the open-risk rule that moves the stop before it fills. The ATR trail of the combined position is
/// the walk's rule (<see cref="AtrTrailRule"/> with no activation), so the stop of the risk rule and the trail are one stop that only tightens. An add is attempted at the close of a bar (<see cref="ReadPyramid"/>) and made, missed or skipped at the
/// open of the first minute that holds a trade after it (<see cref="TryAdd"/>); the walker then starts a new segment of the excursions (<see cref="BeginSegment"/>).
/// </summary>
internal sealed partial class LegState
{
    private readonly AddRules _add;
    private PyramidPlan? _pyramid;
    private List<PartialLeg>? _entryLegs;
    private int _nextLevel;
    private int _pendingLevel = -1;
    private decimal _pendingLimit;
    private decimal _weighted;
    private decimal _entered;
    private decimal _segmentAverage;
    private decimal _maeDone;
    private decimal _mfeDone;

    /// <summary>The walk is a pyramid: it has entry legs and its R, excursions and legs are those of D9.</summary>
    public bool IsPyramid => _pyramid is not null;

    /// <summary>The entry legs so far, the first fill first (a pyramid only).</summary>
    public IReadOnlyList<PartialLeg> EntryLegs => _entryLegs ?? [];

    /// <summary>The adds that filled.</summary>
    public int AddsTaken { get; private set; }

    /// <summary>The adds whose open was beyond the limit.</summary>
    public int AddsMissed { get; private set; }

    /// <summary>The adds the risk rule skipped.</summary>
    public int AddsSkippedRisk { get; private set; }

    /// <summary>The adds whose quantity is 0.</summary>
    public int AddsSkippedQuantity { get; private set; }

    private void StartPyramid()
    {
        if (_policy?.Pyramid is not { } plan)
        {
            return;
        }

        _pyramid = plan;
        _entryLegs = [new PartialLeg(_position.FillEpoch, _position.Entry, _position.EntryFilled, plan.InitialShare, string.Empty)];
        OpenShare = plan.InitialShare;
        _entered = plan.InitialShare;
        _weighted = plan.InitialShare * _position.Entry;
        _segmentAverage = _position.Entry;
    }

    /// <summary>
    /// D2: at a counted bar, with the next level armed (the one before is resolved, no add is waiting) and a close at or beyond it, the add is attempted: it waits for the open of the next minute that holds a trade, with the limit of the close of this
    /// bar. A bar that closes at or after the square-off minute is read at a minute that is itself the square-off, which ends the position before any add (<see cref="TryAdd"/>).
    /// </summary>
    private void ReadPyramid(ExitTape tape, int bar, decimal sign)
    {
        if (_pyramid is not { } plan || _pendingLevel >= 0 || _nextLevel >= plan.MaxAdds || !TickGrid.TryConvert(tape.Close(bar), out var close))
        {
            return;
        }

        if (sign * (close - _position.Entry) < plan.Levels[_nextLevel] * _position.Risk)
        {
            return;
        }

        _pendingLevel = _nextLevel;
        _pendingLimit = _position.IsBuy ? TickGrid.Down(close * (1m + _add.LimitFraction), _add.Tick) : TickGrid.Up(close * (1m - _add.LimitFraction), _add.Tick);
    }

    /// <summary>
    /// D3 and D4: makes the add that is waiting, at the open of this minute. It is not made when the minute is one that ends the position anyway (the square-off, the time stop, or a minute that reaches the stop in force before the add): the stop
    /// exit comes first. Otherwise it is missed (the open is beyond the limit), skipped (no whole share, or the risk rule cannot put the stop below the add's price) or made: the stop becomes the tighter of the stop in force and the level that
    /// keeps the open risk of all legs at 1 R, and the leg is added at the open, slipping like an entry.
    /// </summary>
    /// <param name="minute">The minute that sees the add.</param>
    /// <param name="minuteEpoch">Its start.</param>
    /// <param name="reachesStop">The minute reaches the stop in force before the add (the row X2 decides it, once, in <see cref="MinuteRule"/>).</param>
    /// <returns>True when the add was made.</returns>
    public bool TryAdd(in Minute minute, long minuteEpoch, bool reachesStop)
    {
        if (_pendingLevel < 0 || _pyramid is not { } plan)
        {
            return false;
        }

        var level = _pendingLevel;
        _pendingLevel = -1;
        var isBuy = _position.IsBuy;
        var stop = _position.Stop;
        if (SessionClock.MinuteOfDay(minuteEpoch) >= _add.SquareOffMinute || TimeStopDue || reachesStop)
        {
            return false;
        }

        _nextLevel++;
        var open = minute.Open;
        if (!(isBuy ? open <= _pendingLimit : open >= _pendingLimit))
        {
            AddsMissed++;
            return false;
        }

        var share = plan.Shares[level];
        if (_context.Quantity is { } sizer && Math.Floor(sizer(_position.Entry, _position.Risk) * share) < 1m)
        {
            AddsSkippedQuantity++;
            return false;
        }

        var required = (_weighted + (share * open) - ((isBuy ? 1m : -1m) * _position.Risk)) / (_entered + share);
        if (isBuy ? required >= open : required <= open)
        {
            AddsSkippedRisk++;
            return false;
        }

        _position = _position with { Stop = isBuy ? Math.Max(stop, required) : Math.Min(stop, required) };
        var filled = _position.Slip.Entry(isBuy, open, _pendingLimit, minute.High - minute.Low);
        _entryLegs!.Add(new PartialLeg(minuteEpoch, open, filled, share, string.Empty));
        _weighted += share * open;
        _entered += share;
        OpenShare += share;
        AddsTaken++;
        return true;
    }

    /// <summary>D9: folds the excursions of the segment that ends (the legs open so far, against their average entry) into the maxima and returns the extremes of the next one, which start at the new average entry.</summary>
    public (decimal Low, decimal High) BeginSegment(decimal low, decimal high)
    {
        (_maeDone, _mfeDone) = Excursions(low, high);
        _segmentAverage = _weighted / _entered;
        return (_segmentAverage, _segmentAverage);
    }

    /// <summary>D9: the adverse and favourable excursion in units of the initial risk, the larger of those of the finished segments and of the current one (the extremes since its start against the average entry of its legs).</summary>
    public (decimal Adverse, decimal Favourable) Excursions(decimal low, decimal high)
    {
        var risk = _position.Risk;
        var adverse = _position.IsBuy ? _segmentAverage - low : high - _segmentAverage;
        var favourable = _position.IsBuy ? high - _segmentAverage : _segmentAverage - low;
        return (Math.Max(_maeDone, Math.Max(0m, adverse) / risk), Math.Max(_mfeDone, Math.Max(0m, favourable) / risk));
    }
}
