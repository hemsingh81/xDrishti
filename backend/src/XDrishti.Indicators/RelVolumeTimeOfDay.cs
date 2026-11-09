using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>rel_volume_tod(n)</c> (ADR 0233): the bar's volume divided by the mean volume of the bars at the <b>same time of day</b> in the previous <c>n</c> sessions (the current bar is not in the denominator).
/// The history of each time of day is a ring of the last <c>n</c> values seen at that time, so a missing bar of a session (a hole) is skipped and the ring reaches one session further back; nothing after the
/// current bar is read. Not ready (NaN) until <c>n</c> sessions have been seen at that time of day, while that mean is 0, and for a bar outside the regular session. Volumes are whole numbers, so the sums are exact.
/// A "session" here is a calendar date's occurrence of the slot, not a calendar lookup: a short session (a Saturday special session, a Muhurat session) fills only the slots it has, and a bar outside 09:15 to
/// 15:30 (a Muhurat session in the evening) is not ready and is not remembered. This is a stated shortcut: a special session counts as one of the n sessions at the slots it has.
/// <c>rel_volume(n)</c> is unchanged and still divides by the previous <c>n</c> bars of the timeframe.
/// </summary>
public sealed class RelVolumeTimeOfDay : IIndicator
{
    private readonly int _period;

    private readonly int _barMinutes;

    private readonly int _barsPerSession;

    private readonly long[] _history;

    private readonly long[] _sums;

    private readonly int[] _counts;

    private readonly int[] _next;

    private double _value = double.NaN;

    public RelVolumeTimeOfDay(int period, int barMinutes = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(barMinutes, 1);
        _period = period;
        _barMinutes = barMinutes;
        _barsPerSession = (SessionClock.RegularBars + barMinutes - 1) / barMinutes;
        _history = new long[_barsPerSession * period];
        _sums = new long[_barsPerSession];
        _counts = new int[_barsPerSession];
        _next = new int[_barsPerSession];
    }

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => (_period * _barsPerSession) + 1;

    public void Update(in Bar bar)
    {
        var offset = IndiaDays.MinuteOfDay(bar.EpochSeconds) - SessionClock.OpenMinute;
        if (offset < 0 || offset >= SessionClock.RegularBars)
        {
            _value = double.NaN;
            return;
        }

        var slot = offset / _barMinutes;
        var at = (slot * _period) + _next[slot];
        if (_counts[slot] >= _period)
        {
            _value = _sums[slot] > 0 ? bar.Volume * (double)_period / _sums[slot] : double.NaN;
            _sums[slot] -= _history[at];
        }
        else
        {
            _value = double.NaN;
            _counts[slot]++;
        }

        _history[at] = bar.Volume;
        _sums[slot] += bar.Volume;
        _next[slot] = _next[slot] + 1 == _period ? 0 : _next[slot] + 1;
    }

    public void Reset()
    {
        Array.Clear(_history);
        Array.Clear(_sums);
        Array.Clear(_counts);
        Array.Clear(_next);
        _value = double.NaN;
    }
}
