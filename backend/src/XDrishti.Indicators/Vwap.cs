using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>vwap</c>: the volume-weighted average price since the session open, <c>Σ(typical·volume) / Σvolume</c> with <c>typical = (high + low + close)/3</c>. It resets at the first bar of each India
/// trading date and is <see cref="double.NaN"/> until the session has traded volume (zero-volume bars add nothing).
/// </summary>
public sealed class Vwap : IIndicator
{
    private CompensatedSum _priceVolume;

    private long _volume;

    private long _day;

    private bool _started;

    public double Value => IsReady ? _priceVolume.Value / _volume : double.NaN;

    public bool IsReady => _volume > 0;

    public int WarmUp => 1;

    public void Update(in Bar bar)
    {
        var day = IndiaDays.DayOf(bar.EpochSeconds);
        if (!_started || day != _day)
        {
            _priceVolume.Clear();
            _volume = 0;
            _day = day;
            _started = true;
        }

        if (bar.Volume > 0)
        {
            _priceVolume.Add((bar.High + bar.Low + bar.Close) / 3.0 * bar.Volume);
            _volume += bar.Volume;
        }
    }

    public void Reset()
    {
        _priceVolume.Clear();
        _volume = 0;
        _day = 0;
        _started = false;
    }
}
