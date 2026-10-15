using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>or_high(m)</c>: the highest high of the first <c>m</c> minutes of the session (bars 09:15 … 09:15 + m − 1). Not ready until the last of those minutes has closed, then constant until the session
/// ends; it resets each India trading date. On a higher timeframe (<c>barMinutes</c> &gt; 1) <c>m</c> must be a whole number of bars.
/// </summary>
public sealed class OrHigh : IIndicator
{
    private readonly int _warmUp;

    private OpeningRange _range;

    public OrHigh(int minutes, int barMinutes = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(barMinutes, 1);
        _range = new OpeningRange(minutes, barMinutes);
        _warmUp = (minutes + barMinutes - 1) / barMinutes;
    }

    public double Value => _range.High;

    public bool IsReady => _range.IsReady;

    public int WarmUp => _warmUp;

    public void Update(in Bar bar) => _range.Update(bar);

    public void Reset() => _range.Clear();
}
