using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>or_high(m)</c>: the highest high of the first <c>m</c> minutes of the session (bars 09:15 … 09:15 + m − 1). Not ready until the last of those minutes has closed, then constant until the session
/// ends; it resets each India trading date. On a higher timeframe (<c>barMinutes</c> &gt; 1) <c>m</c> must be a whole number of bars. When the frame has moved to a new date and this instance has not been
/// fed a bar of it yet (a coarser timeframe), it is not ready (ADR 0035).
/// </summary>
public sealed class OrHigh : IIndicator, IRestartingIndicator
{
    private readonly int _warmUp;

    private OpeningRange _range;

    private bool _pending;

    public OrHigh(int minutes, int barMinutes = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(barMinutes, 1);
        _range = new OpeningRange(minutes, barMinutes);
        _warmUp = (minutes + barMinutes - 1) / barMinutes;
    }

    public double Value => IsReady ? _range.High : double.NaN;

    public bool IsReady => !_pending && _range.IsReady;

    public int WarmUp => _warmUp;

    public void Update(in Bar bar)
    {
        _pending = false;
        _range.Update(bar);
    }

    public void Reset()
    {
        _pending = false;
        _range.Clear();
    }

    void IRestartingIndicator.MarkPending() => _pending = true;
}
